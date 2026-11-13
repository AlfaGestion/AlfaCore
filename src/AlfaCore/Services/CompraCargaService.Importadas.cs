using System.Globalization;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Comprobantes importados de ARCA (Control ARCA → Mis Comprobantes Recibidos, usuario IMPORTACION): ya tienen cabecera y asiento pero no el
// detalle del comprobante físico. Al llegar el comprobante se lo carga desde esta pantalla y reemplaza a la importación, siempre que los
// totales coincidan con lo informado por ARCA. El legacy no tiene este circuito: solo avisa "YA CARGADO EN COMPRAS" sin bloquear.
public sealed partial class CompraCargaService
{
    private const string UsuarioImportacionArca="IMPORTACION";
    private const decimal ToleranciaArcaPorDefecto=1.00m;

    private sealed class ImportadaFila
    {
        public int Id{get;set;}public string Tipo{get;set;}="";public string Comprobante{get;set;}="";public string Cuenta{get;set;}="";public DateTime Fecha{get;set;}
        public DateTime? Vencimiento{get;set;}public string Unidad{get;set;}="";public string Usuario{get;set;}="";public bool Anulada{get;set;}public bool Protegida{get;set;}
        public decimal Total{get;set;}public decimal Iva{get;set;}public decimal NetoGravado{get;set;}
    }
    private sealed class AsientoImportado
    {
        public string Periodo{get;set;}="";public int Mes{get;set;}public int Asiento{get;set;}public string TipoRegistro{get;set;}="";public bool Contabilizado{get;set;}public bool Conciliado{get;set;}public string Unidad{get;set;}="";public string Tc{get;set;}="";public string Numero{get;set;}="";public string Letra{get;set;}="";
    }

    /// <summary>Id de comprobante con el formato del sistema: sucursal de 4, número de 8 y letra.</summary>
    internal static string IdComprobanteCompra(string? sucursal,string? numero,string? letra)
    {
        static string Digitos(string? t,int ancho){var d=new string((t??"").Where(char.IsAsciiDigit).ToArray());return d.Length>=ancho?d[^ancho..]:d.PadLeft(ancho,'0');}
        return Digitos(sucursal,4)+Digitos(numero,8)+(letra??"").Trim();
    }

    /// <summary>Busca una compra del mismo proveedor, tipo y número, incluidas las anuladas y las importadas de ARCA.</summary>
    public Task<CompraExistente?> CompraExistenteAsync(string proveedor,string tipo,string letra,string sucursal,string numero,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar<CompraExistente?>(baseEsperada,"CompraExistente",(c,t)=>BuscarExistenteAsync(c,proveedor,tipo,letra,sucursal,numero,t),ct,registrarErrores:false);

    private static async Task<decimal> ToleranciaArcaAsync(SqlConnection c,CancellationToken t,SqlTransaction? tx=null)
    {
        var cfg=await ConfiguracionAsync(c,["COMPRAS-ARCA-TOLERANCIA"],t,tx);
        return decimal.TryParse(cfg.GetValueOrDefault("COMPRAS-ARCA-TOLERANCIA")?.Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out var v)&&v>=0?v:ToleranciaArcaPorDefecto;
    }

    private static async Task<CompraExistente?> BuscarExistenteAsync(SqlConnection c,string proveedor,string tipo,string letra,string sucursal,string numero,CancellationToken t)
    {
        var id=IdComprobanteCompra(sucursal,numero,letra);
        if(proveedor.Trim().Length==0||tipo.Trim().Length==0||id.Length<13)return null;
        var existente=await c.QueryFirstOrDefaultAsync<CompraExistente>(Cmd("""
            SELECT TOP(1) f.ID Id,LTRIM(RTRIM(f.TC)) Tipo,LTRIM(RTRIM(f.IDCOMPROBANTE)) Comprobante,f.FECHA Fecha,LTRIM(RTRIM(ISNULL(f.USUARIO,''))) Usuario,
              f.IMPORTE Total,ISNULL(f.ImporteIva,0) Iva,ISNULL(f.NetoGravado,0) NetoGravado,CONVERT(bit,ISNULL(f.ANULADA,0)) Anulada
            FROM dbo.C_MV_CPTE f WHERE LTRIM(RTRIM(f.CUENTA))=@cuenta AND LTRIM(RTRIM(f.TC))=@tipo AND LTRIM(RTRIM(f.IDCOMPROBANTE))=@id ORDER BY f.ID DESC;
            """,new{cuenta=proveedor.Trim(),tipo=tipo.Trim(),id},t));
        if(existente is {ImportadaArca:true})existente.Tolerancia=await ToleranciaArcaAsync(c,t);
        return existente;
    }

    /// <summary>Importadas de ARCA que todavía esperan su comprobante físico: las más recientes primero.</summary>
    public Task<IReadOnlyList<CompraImportadaPendiente>> ImportadasPendientesAsync(int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar<IReadOnlyList<CompraImportadaPendiente>>(baseEsperada,"ImportadasPendientes",async(c,t)=>
        {
            var contexto=await Contexto(c,t);
            var unidades=contexto.Unidades.Select(u=>u.Codigo.Trim()).ToArray();
            if(unidades.Length==0)return [];
            return (await c.QueryAsync<CompraImportadaPendiente>(Cmd("""
                SELECT TOP(200) f.ID Id,LTRIM(RTRIM(f.TC)) Tipo,LTRIM(RTRIM(f.IDCOMPROBANTE)) Comprobante,LTRIM(RTRIM(f.CUENTA)) Cuenta,ISNULL(LTRIM(RTRIM(f.NOMBRE)),'') Proveedor,f.FECHA Fecha,f.IMPORTE Total
                FROM dbo.C_MV_CPTE f WHERE LTRIM(RTRIM(ISNULL(f.USUARIO,'')))=@usuario AND ISNULL(f.ANULADA,0)=0 AND LTRIM(RTRIM(f.TC)) IN ('FCC','NCC','NDC')
                  AND (LTRIM(RTRIM(ISNULL(f.UNEGOCIO,'')))='' OR LTRIM(RTRIM(f.UNEGOCIO)) IN @unidades)
                ORDER BY f.FECHA DESC,f.ID DESC;
                """,new{usuario=UsuarioImportacionArca,unidades},t))).AsList();
        },ct);

    /// <summary>Borrador con la cabecera de la importación (proveedor, tipo, número, fecha, total) listo para cargar el detalle del comprobante.</summary>
    public Task<CompraCargaBorrador> PrepararDesdeImportadaAsync(int compra,int? baseEsperada=null,CancellationToken ct=default)
        =>Ejecutar(baseEsperada,"PrepararDesdeImportada",async(c,t)=>
        {
            var contexto=await Contexto(c,t);
            var f=await c.QuerySingleOrDefaultAsync<ImportadaFila>(Cmd(ImportadaSql,new{id=compra},t))??throw new InvalidOperationException("La compra importada ya no existe.");
            if(!string.Equals(f.Usuario,UsuarioImportacionArca,StringComparison.OrdinalIgnoreCase)||f.Anulada)throw new InvalidOperationException("Esa compra ya no es una importación de ARCA pendiente de completar.");
            if(f.Tipo is not("FCC" or "NCC" or "NDC")||f.Comprobante.Trim().Length<13)throw new InvalidOperationException("Solo se pueden completar facturas y notas de compra importadas.");
            // El importador viejo del legacy no asignaba unidad de negocio: en ese caso la toma la carga.
            if(f.Unidad.Length>0&&!contexto.Unidades.Any(u=>u.Codigo.Trim()==f.Unidad))throw new InvalidOperationException("La unidad de negocio de la compra no está autorizada.");
            var comprobante=f.Comprobante.Trim();
            return new CompraCargaBorrador{Tipo=f.Tipo,Proveedor=f.Cuenta,Unidad=f.Unidad.Length>0?f.Unidad:contexto.Unidad,Motivo=contexto.Motivo,Letra=comprobante[^1..],Sucursal=comprobante[..4],Numero=comprobante.Substring(4,8),
                Fecha=f.Fecha.Date,FechaContable=f.Fecha.Date,Vencimiento=(f.Vencimiento??f.Fecha).Date,TotalDocumento=f.Total,ReemplazaImportadaId=f.Id,ServiciosGastosConfirmados=false};
        },ct);

    private const string ImportadaSql="""
        SELECT f.ID Id,LTRIM(RTRIM(f.TC)) Tipo,LTRIM(RTRIM(f.IDCOMPROBANTE)) Comprobante,LTRIM(RTRIM(f.CUENTA)) Cuenta,f.FECHA Fecha,f.VENCIMIENTO Vencimiento,LTRIM(RTRIM(ISNULL(f.UNEGOCIO,''))) Unidad,
          LTRIM(RTRIM(ISNULL(f.USUARIO,''))) Usuario,CONVERT(bit,ISNULL(f.ANULADA,0)) Anulada,f.IMPORTE Total,ISNULL(f.ImporteIva,0) Iva,ISNULL(f.NetoGravado,0) NetoGravado,
          CONVERT(bit,CASE WHEN ISNULL(f.Bloqueado,0)=1 OR ISNULL(f.Cerrada,0)=1 THEN 1 ELSE 0 END) Protegida
        FROM dbo.C_MV_CPTE f WITH(UPDLOCK,HOLDLOCK) WHERE f.ID=@id;
        """;

    /// <summary>
    /// Retira la importación de ARCA que esta carga reemplaza, dentro de la transacción del registro. Exige el mismo proveedor, tipo, número, fecha
    /// y unidad, que nada esté bloqueado, contabilizado, conciliado ni aplicado, y que total, IVA y neto gravado coincidan con ARCA
    /// (tolerancia COMPRAS-ARCA-TOLERANCIA). Guarda una copia del asiento retirado para poder auditarlo o reconstruirlo.
    /// </summary>
    private async Task RetirarImportadaAsync(SqlConnection cn,SqlTransaction tx,CompraCargaBorrador d,CompraCargaProveedor proveedor,CompraCargaContexto contexto,CompraCargaVista vista,string idComprobante,string equivalencia,CancellationToken ct)
    {
        CommandDefinition Q(string sql,object? args=null)=>Cmd(sql,args,ct,tx);
        var imp=await cn.QuerySingleOrDefaultAsync<ImportadaFila>(Q(ImportadaSql,new{id=d.ReemplazaImportadaId}))??throw new InvalidOperationException("La compra importada de ARCA ya no existe.");
        if(!string.Equals(imp.Usuario,UsuarioImportacionArca,StringComparison.OrdinalIgnoreCase)||imp.Anulada)
            throw new InvalidOperationException("Esa compra ya no es una importación de ARCA pendiente de completar. Volvé a abrir el comprobante.");
        if(imp.Cuenta!=proveedor.Codigo.Trim()||imp.Tipo!=d.Tipo.Trim()||imp.Comprobante.Trim()!=idComprobante)
            throw new InvalidOperationException("El proveedor, el tipo y el número tienen que ser los de la importación de ARCA que se completa.");
        if(imp.Fecha.Date!=d.Fecha.Date)throw new InvalidOperationException($"La fecha tiene que ser la de la importación de ARCA ({imp.Fecha:dd/MM/yyyy}).");
        if(imp.Unidad.Length>0&&imp.Unidad!=d.Unidad.Trim())throw new InvalidOperationException("La unidad de negocio tiene que ser la de la importación de ARCA.");
        if(imp.Protegida)throw new InvalidOperationException("La compra importada está bloqueada o cerrada. Revisala en el legacy.");
        if(contexto.Cierre is {} cierre&&imp.Fecha.Date<=cierre.Date||contexto.Desde is {} desde&&imp.Fecha.Date<desde.Date||contexto.Hasta is {} hasta&&imp.Fecha.Date>hasta.Date)
            throw new InvalidOperationException("La compra importada está dentro del cierre o fuera del rango operativo.");
        // El asiento se busca por tipo, número, letra, proveedor y fecha: el importador viejo del legacy a veces guardó otra sucursal que la cabecera.
        // Los totales tienen que cerrar con ARCA: es el control que justifica reemplazar un asiento ya informado.
        var tolerancia=await ToleranciaArcaAsync(cn,ct,tx);
        var ivaNuevo=vista.Bases.Sum(b=>b.Iva);var netoNuevo=vista.Bases.Where(b=>b.Tratamiento=="Gravado").Sum(b=>b.Neto);
        var diferencias=new List<string>();
        void Comparar(string nombre,decimal arca,decimal nuevo){if(Math.Abs(arca-nuevo)>tolerancia)diferencias.Add($"{nombre}: ARCA {arca:N2} · esta carga {nuevo:N2}");}
        Comparar("Total",imp.Total,vista.Total);Comparar("IVA",imp.Iva,ivaNuevo);Comparar("Neto gravado",imp.NetoGravado,netoNuevo);
        if(diferencias.Count>0)
            throw new InvalidOperationException("Los importes no coinciden con lo informado por ARCA ("+string.Join("; ",diferencias)+"). Corregí los renglones hasta que cierren (tolerancia "+tolerancia.ToString("N2",CultureInfo.GetCultureInfo("es-AR"))+").");
        var asientos=(await cn.QueryAsync<AsientoImportado>(Q("""
            SELECT LTRIM(RTRIM(PERIODO)) Periodo,MES_OPERATIVO Mes,[NUMERO ASIENTO] Asiento,LTRIM(RTRIM(TIPO_REG)) TipoRegistro,CONVERT(bit,ISNULL(CONTABILIZADO,0)) Contabilizado,
              CONVERT(bit,ISNULL(Conciliado,0)) Conciliado,LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad,LTRIM(RTRIM(TC)) Tc,NUMERO Numero,LETRA Letra
            FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(TC)) IN (@tc,@equivalencia) AND NUMERO=@numero AND LETRA=@letra AND FECHA=@fecha
              AND LTRIM(RTRIM(ISNULL(CABCUENTA,'')))=@cuenta AND SECUENCIA=1;
            """,new{tc=d.Tipo.Trim(),equivalencia=equivalencia.Trim(),numero=idComprobante.Substring(4,8),letra=idComprobante[12..],fecha=imp.Fecha.Date,cuenta=proveedor.Codigo.Trim()}))).AsList();
        if(asientos.Count!=1)throw new InvalidOperationException("No se encontró un único asiento de la importación de ARCA. Revisalo en contabilidad antes de completarla.");
        var a=asientos[0];
        if(a.Contabilizado||a.Conciliado||a.Unidad.Length>0&&a.Unidad!=d.Unidad.Trim())throw new InvalidOperationException("El asiento importado está contabilizado, conciliado o incluye otra unidad. No puede reemplazarse aquí.");
        if(await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.MV_EJERCICIOS WHERE LTRIM(RTRIM(CONVERT(nvarchar(6),PERIODO)))=@Periodo AND ISNULL(CERRADO,0)=0;",a))!=1)
            throw new InvalidOperationException("El ejercicio de la importación está cerrado.");
        if(await cn.ExecuteScalarAsync<int>(Q("""
            SELECT COUNT(*) FROM dbo.MV_APLICACION WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(CUENTA))=@cuenta AND
              ((LTRIM(RTRIM(TC)) IN (@tc,@equivalencia) AND (LTRIM(RTRIM(IDComprobante))=@id OR SUCURSAL+NUMERO+LETRA=@id)) OR
               (LTRIM(RTRIM(TCO_ORIGEN)) IN (@tc,@equivalencia) AND (LTRIM(RTRIM(IDComprobante_Origen))=@id OR SUCURSAL_ORIGEN+NUMERO_ORIGEN+LETRA_ORIGEN=@id)));
            """,new{cuenta=proveedor.Codigo.Trim(),tc=d.Tipo.Trim(),equivalencia=equivalencia.Trim(),id=idComprobante}))>0)
            throw new InvalidOperationException("La compra importada tiene aplicaciones, pagos o documentos asociados. Primero deben resolverse en su circuito.");
        var filasAsiento=(await cn.QueryAsync(Q("SELECT * FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro AND LTRIM(RTRIM(TC))=@Tc AND NUMERO=@Numero AND LETRA=@Letra ORDER BY SECUENCIA;",a))).ToList();
        var centros=(await cn.QueryAsync(Q("SELECT * FROM dbo.MV_ASIENTOSCCOSTO c WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(Tipo_Reg))=@TipoRegistro AND EXISTS(SELECT 1 FROM dbo.MV_ASIENTOS x WHERE LTRIM(RTRIM(x.PERIODO))=@Periodo AND x.MES_OPERATIVO=@Mes AND x.[NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(x.TIPO_REG))=@TipoRegistro AND x.CUENTA=c.CUENTA AND x.SECUENCIA=c.SECUENCIA AND LTRIM(RTRIM(x.TC))=@Tc AND x.NUMERO=@Numero AND x.LETRA=@Letra) ORDER BY SECUENCIA;",a))).ToList();
        await cn.ExecuteAsync(Q("""
            IF EXISTS(SELECT 1 FROM dbo.ALFACORE_COMPRA_ANULACION WHERE CompraId=@compra) DELETE dbo.ALFACORE_COMPRA_ANULACION WHERE CompraId=@compra;
            INSERT dbo.ALFACORE_COMPRA_ANULACION(CompraId,Usuario,Motivo,AsientoJson,CentrosJson) VALUES(@compra,@usuario,@motivo,@asientos,@centros);
            """,new{compra=imp.Id,usuario=usuarios.GetCurrentUserName(),motivo="Importación de ARCA reemplazada por la carga del comprobante con su detalle.",asientos=JsonSerializer.Serialize(filasAsiento),centros=JsonSerializer.Serialize(centros)}));
        await cn.ExecuteAsync(Q("""
            -- El número de asiento se repite entre comprobantes: se borra solo el de esta importación (TC + número + letra).
            DELETE c FROM dbo.MV_ASIENTOSCCOSTO c WHERE LTRIM(RTRIM(c.PERIODO))=@Periodo AND c.MES_OPERATIVO=@Mes AND c.[NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(c.Tipo_Reg))=@TipoRegistro
              AND EXISTS(SELECT 1 FROM dbo.MV_ASIENTOS x WHERE LTRIM(RTRIM(x.PERIODO))=@Periodo AND x.MES_OPERATIVO=@Mes AND x.[NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(x.TIPO_REG))=@TipoRegistro AND x.CUENTA=c.CUENTA AND x.SECUENCIA=c.SECUENCIA AND LTRIM(RTRIM(x.TC))=@Tc AND x.NUMERO=@Numero AND x.LETRA=@Letra);
            DELETE dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND LTRIM(RTRIM(TIPO_REG))=@TipoRegistro
              AND LTRIM(RTRIM(TC))=@Tc AND NUMERO=@Numero AND LETRA=@Letra;
            """,a));
        await cn.ExecuteAsync(Q("DELETE dbo.C_MV_CPTE WHERE ID=@id AND LTRIM(RTRIM(ISNULL(USUARIO,'')))=@usuario;",new{id=imp.Id,usuario=UsuarioImportacionArca}));
    }
}
