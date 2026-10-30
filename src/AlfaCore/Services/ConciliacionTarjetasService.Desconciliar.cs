using System.Data;
using System.Globalization;
using System.Xml.Linq;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    internal sealed record ClaveDeshacer(long VinculoId,string Cuenta,string Clave);
    internal static string ClavesDeshacerXml(IEnumerable<ClaveDeshacer> claves)
    {
        return new XElement("claves",claves.Select(c=>
        {
            var partes=c.Clave.Split('|');
            var mes=0;var asiento=0;var secuencia=0;
            var valida=partes.Length is 5 or 9 && c.Cuenta.Trim().Length is >0 and <=15
                && int.TryParse(partes[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out mes) && mes is >=0 and <=255
                && int.TryParse(partes[2],NumberStyles.Integer,CultureInfo.InvariantCulture,out asiento)
                && int.TryParse(partes[4],NumberStyles.Integer,CultureInfo.InvariantCulture,out secuencia);
            string Parte(int indice)=>valida&&partes.Length>indice?partes[indice]:"";
            return new XElement("c",new XAttribute("vinculo",c.VinculoId),new XAttribute("cuenta",c.Cuenta.Trim()),
                new XAttribute("clave",c.Clave),new XAttribute("valida",valida?1:0),new XAttribute("completa",partes.Length==9?1:0),
                new XAttribute("periodo",Parte(0)),new XAttribute("mes",valida?mes:0),new XAttribute("asiento",valida?asiento:0),
                new XAttribute("tipo",Parte(3)),new XAttribute("secuencia",valida?secuencia:0),
                new XAttribute("tc",Parte(5)),new XAttribute("sucursal",Parte(6)),new XAttribute("numero",Parte(7)),new XAttribute("letra",Parte(8)));
        })).ToString(SaveOptions.DisableFormatting);
    }

    // CUENTA admite 15 caracteres. Conservar la lectura de cuentas legacy con blancos iniciales
    // mediante búsquedas por igualdad, sin aplicar LTRIM a la primera columna de la PK.
    private static readonly string CuentasFisicasDeshacerSql=string.Join(",",Enumerable.Range(0,15)
        .Select(n=>n==0?"k.Cuenta":$"SPACE({n})+k.Cuenta"));
    public Task<TarjetasVistaDeshacer> VinculosParaDeshacerAsync(long importacion,CancellationToken ct=default)
        =>Ejecutar("ConsultarDesconciliacion",(c,t)=>CargarVinculosParaDeshacer(c,importacion,t),ct);

    // La identidad es la misma de Comparar. Para deshacer no se aplican filtros de fecha,
    // sucursal, texto, tipo de comprobante ni marcas de liquidación; sí los permisos por unidad.
    internal static readonly string AutorizarDesconciliacionSql=$"""
        IF NOT EXISTS(SELECT 1 FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@usuario))
            THROW 50001,'El usuario no existe en la base activa.',1;
        DECLARE @xml xml=CONVERT(xml,@clavesXml);
        ;WITH claves AS (
            SELECT n.c.value('@vinculo','bigint') VinculoId,n.c.value('@cuenta','nvarchar(15)') Cuenta,
                n.c.value('@clave','nvarchar(200)') Clave,n.c.value('@valida','bit') Valida,n.c.value('@completa','bit') Completa,
                n.c.value('@periodo','nvarchar(32)') Periodo,n.c.value('@mes','tinyint') Mes,
                n.c.value('@asiento','int') Asiento,n.c.value('@tipo','nvarchar(32)') Tipo,
                n.c.value('@secuencia','int') Secuencia,n.c.value('@tc','nvarchar(32)') Tc,
                n.c.value('@sucursal','nvarchar(32)') Sucursal,n.c.value('@numero','nvarchar(32)') Numero,
                n.c.value('@letra','nvarchar(32)') Letra
            FROM @xml.nodes('/claves/c') n(c)
        )
        SELECT v.VinculoId AS Id,COUNT(*) AS Movimientos,
            SUM(CASE WHEN LTRIM(RTRIM(v.Cuenta))=@cuenta AND p.Cantidad=1 AND p.Permitidos=1 THEN 1 ELSE 0 END) AS Autorizados
        FROM dbo.TJ_VinculoAlfa v
        INNER JOIN dbo.TJ_Vinculo h ON h.Id=v.VinculoId AND h.ImportacionId=@importacion AND h.Activo=1
        LEFT JOIN claves k ON k.VinculoId=v.VinculoId AND k.Cuenta=LTRIM(RTRIM(v.Cuenta)) AND k.Clave=v.Clave
        OUTER APPLY (
            SELECT COUNT(*) AS Cantidad,SUM(ISNULL(permiso.Autorizado,0)) AS Permitidos
            FROM dbo.MV_ASIENTOS a
            OUTER APPLY (SELECT TOP(1) 1 AS Autorizado FROM dbo.TA_USUARIOS u
                WHERE UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@usuario)
                  AND (ISNULL(u.AdminCentral,0)=1 OR
                      (NULLIF(LTRIM(RTRIM(u.UNEGOCIO)),'') IS NOT NULL
                       AND LTRIM(RTRIM(u.UNEGOCIO))=LTRIM(RTRIM(a.UNEGOCIO))))) permiso
            WHERE k.Valida=1 AND a.CUENTA IN ({CuentasFisicasDeshacerSql})
              AND a.SECUENCIA=k.Secuencia AND a.MES_OPERATIVO=k.Mes AND a.[NUMERO ASIENTO]=k.Asiento
              AND a.PERIODO=k.Periodo AND a.TIPO_REG=k.Tipo
              AND ((k.Completa=1 AND a.TC=k.Tc AND a.SUCURSAL=k.Sucursal AND a.NUMERO=k.Numero AND a.LETRA=k.Letra
                        AND {ClaveMovimientoSql}=v.Clave)
                OR (k.Completa=0 AND {ClaveAnteriorMovimientoSql}=v.Clave AND {HuellaMovimientoSql}=v.Huella))
        ) p
        WHERE v.Activo=1
        GROUP BY v.VinculoId;
        """;

    private async Task<TarjetasVistaDeshacer> CargarVinculosParaDeshacer(SqlConnection c,long importacion,CancellationToken ct,SqlTransaction? tx=null)
    {
        var cuenta=await c.ExecuteScalarAsync<string?>(Cmd("SELECT Cuenta FROM dbo.TJ_Importacion WHERE Id=@importacion",new{importacion},ct,tx))
            ??throw new InvalidOperationException("No se encontró la importación.");
        var grupos=(await c.QueryAsync<TarjetasVinculoDeshacer>(Cmd("""
            SELECT Id,Usuario,FechaUtc,ImporteResumen,ImporteAlfa
            FROM dbo.TJ_Vinculo WITH(UPDLOCK,HOLDLOCK)
            WHERE ImportacionId=@importacion AND Activo=1 ORDER BY Id;
            """,new{importacion},ct,tx))).AsList();
        var detalles=await c.QueryAsync<TarjetasVinculoDeshacer>(Cmd("""
            SELECT v.VinculoId AS Id,COUNT(*) AS Detalles,
                SUM(CASE WHEN d.ImportacionId=@importacion THEN 1 ELSE 0 END) AS DetallesValidos,
                MIN(d.Fecha) AS Desde,MAX(d.Fecha) AS Hasta
            FROM dbo.TJ_VinculoDetalle v
            INNER JOIN dbo.TJ_Vinculo h ON h.Id=v.VinculoId AND h.ImportacionId=@importacion AND h.Activo=1
            LEFT JOIN dbo.TJ_Detalle d ON d.Id=v.DetalleId
            WHERE v.Activo=1 GROUP BY v.VinculoId;
            """,new{importacion},ct,tx));
        var claves=await c.QueryAsync<ClaveDeshacer>(Cmd("""
            SELECT v.VinculoId,v.Cuenta,v.Clave FROM dbo.TJ_VinculoAlfa v
            INNER JOIN dbo.TJ_Vinculo h ON h.Id=v.VinculoId
            WHERE h.ImportacionId=@importacion AND h.Activo=1 AND v.Activo=1;
            """,new{importacion},ct,tx));
        var autorizaciones=await c.QueryAsync<TarjetasVinculoDeshacer>(Cmd(AutorizarDesconciliacionSql,
            new{importacion,cuenta=cuenta.Trim(),usuario=users.GetCurrentUserName(),clavesXml=ClavesDeshacerXml(claves)},ct,tx));
        var porDetalle=detalles.ToDictionary(d=>d.Id);
        var porAlfa=autorizaciones.ToDictionary(d=>d.Id);
        foreach(var grupo in grupos)
        {
            if(porDetalle.TryGetValue(grupo.Id,out var d))
            {grupo.Detalles=d.Detalles;grupo.DetallesValidos=d.DetallesValidos;grupo.Desde=d.Desde;grupo.Hasta=d.Hasta;}
            if(porAlfa.TryGetValue(grupo.Id,out var a))
            {grupo.Movimientos=a.Movimientos;grupo.Autorizados=a.Autorizados;}
        }
        return new(importacion,cuenta.Trim(),grupos);
    }

    internal static IReadOnlyList<TarjetasVinculoDeshacer> ValidarDesconciliaciones(TarjetasVistaDeshacer actual,TarjetasDeshacerVarias solicitud)
    {
        if(string.IsNullOrWhiteSpace(solicitud.Motivo)||solicitud.Motivo.Length>500)
            throw new InvalidOperationException("Escribí un motivo de hasta 500 caracteres.");
        if(solicitud.Importacion!=actual.Importacion||!string.Equals(solicitud.Cuenta.Trim(),actual.Cuenta,StringComparison.Ordinal))
            throw new InvalidOperationException("La cuenta o importación cambió. Volvé a consultar las conciliaciones.");
        if(solicitud.Vinculos.Count==0||solicitud.Vinculos.Count>1000||solicitud.Vinculos.Any(id=>id<=0)
            ||solicitud.Vinculos.Distinct().Count()!=solicitud.Vinculos.Count)
            throw new InvalidOperationException("Seleccioná entre 1 y 1.000 conciliaciones diferentes para deshacer.");
        var porId=actual.Vinculos.ToDictionary(v=>v.Id);
        var elegidos=new List<TarjetasVinculoDeshacer>();
        foreach(var id in solicitud.Vinculos.OrderBy(id=>id))
        {
            if(!porId.TryGetValue(id,out var grupo))
                throw new InvalidOperationException($"La conciliación #{id} no pertenece a esta importación o ya fue anulada. Actualizá la lista.");
            if(!grupo.PuedeDeshacer)
                throw new InvalidOperationException($"No se puede deshacer la conciliación #{id}: hay movimientos no identificables o sin permiso sobre sus unidades. No se deshizo ninguna conciliación.");
            elegidos.Add(grupo);
        }
        return elegidos;
    }

    public Task<int> DesconciliarVariasAsync(TarjetasDeshacerVarias solicitud,CancellationToken ct=default)
        =>Ejecutar("DesconciliarVarias",async(c,t)=>
        {
            var session=sessions.GetActiveSession()!.Id;
            using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            var cantidad=await DesconciliarEnTransaccion(c,tx,solicitud,t);
            AntesDeGrabar(session,t);tx.Commit();return cantidad;
        },ct);

    public Task<bool> DesconciliarAsync(long id,TarjetasFiltro filtro,string motivo,CancellationToken ct=default)
        =>Ejecutar("Desconciliar",async(c,t)=>
        {
            var session=sessions.GetActiveSession()!.Id;
            using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            var actual=await CargarVinculosParaDeshacer(c,filtro.Importacion,t,tx);
            var solicitud=new TarjetasDeshacerVarias(filtro.Importacion,actual.Cuenta,[id],motivo);
            await DesconciliarEnTransaccion(c,tx,solicitud,t,actual);
            AntesDeGrabar(session,t);tx.Commit();return true;
        },ct);

    internal async Task<int> DesconciliarEnTransaccion(SqlConnection c,SqlTransaction tx,TarjetasDeshacerVarias solicitud,CancellationToken ct,TarjetasVistaDeshacer? actual=null)
    {
        actual??=await CargarVinculosParaDeshacer(c,solicitud.Importacion,ct,tx);
        var elegidos=ValidarDesconciliaciones(actual,solicitud);
        // Todos los grupos se validan antes de la primera escritura. Solo se anulan auxiliares TJ.
        foreach(var grupo in elegidos)
        {
            await c.ExecuteAsync(Cmd("""
                UPDATE dbo.TJ_Vinculo SET Activo=0,UsuarioAnulacion=@usuario,FechaAnulacion=SYSUTCDATETIME(),MotivoAnulacion=@motivo
                    WHERE Id=@id AND ImportacionId=@importacion AND Activo=1;
                UPDATE dbo.TJ_VinculoDetalle SET Activo=0 WHERE VinculoId=@id AND Activo=1;
                UPDATE dbo.TJ_VinculoAlfa SET Activo=0 WHERE VinculoId=@id AND Activo=1;
                """,new{id=grupo.Id,solicitud.Importacion,motivo=solicitud.Motivo.Trim(),usuario=users.GetCurrentUserName()},ct,tx));
        }
        return elegidos.Count;
    }
}
