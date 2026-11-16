using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Cobranza directa (módulo Cobranzas): recibo CB sin movimiento bancario. Reutiliza el motor de
// grabación de la conciliación; solo cambian el permiso, los medios admitidos (suma efectivo) y
// el origen de los importes, que carga el usuario.
public sealed partial class CobranzasService
{
    // Misma clave que la opción Cobranzas (D6003) del menú Ventas del legacy.
    public const string MenuKey = "D6003";

    // IMPORTE de MV_ASIENTOS está siempre en moneda local (MONEDA/COTIZACION son referencia):
    // el saldo es una única suma, igual que VE_CPTES_SALDOS_VENTAS del legacy.
    private static async Task<IReadOnlyList<CobranzaSaldoCliente>> SaldoClienteAsync(SqlConnection cn,string cliente,DateTime corte,
        Usuario user,SqlTransaction? tx,CancellationToken ct)
        => (await cn.QueryAsync<CobranzaSaldoCliente>(Cmd("""
            SELECT @cliente Cliente,
                ISNULL((SELECT TOP(1) NULLIF(LTRIM(RTRIM(mon.DESCRIPCION)),'') FROM dbo.TA_MONEDAS mon WHERE LTRIM(RTRIM(mon.CODIGO))='1'),'$') Moneda,
                CONVERT(decimal(19,4),SUM(CASE WHEN a.[DEBE-HABER]='D' THEN a.IMPORTE WHEN a.[DEBE-HABER]='H' THEN -a.IMPORTE ELSE 0 END)) Saldo
            FROM dbo.MV_ASIENTOS a
            WHERE LTRIM(RTRIM(a.CUENTA))=@cliente AND a.EsResumen=0 AND a.FECHA<@hasta
                AND (@admin=1 OR LTRIM(RTRIM(a.UNEGOCIO))=@unidad)
            HAVING COUNT(*)>0;
            """,new { cliente,hasta=corte.Date.AddDays(1),admin=user.Admin,unidad=user.Unidad },tx,ct))).AsList();

    // Clientes con comprobantes pendientes en pesos, agrupados por cliente y unidad de negocio
    // (un recibo no mezcla unidades). Solo se ven las unidades autorizadas para el usuario.
    public Task<IReadOnlyList<CobranzaPagadorGrupo>> BuscarClientesPendientesAsync(CobranzaFiltro filtro,CancellationToken ct=default)
        => Ejecutar<IReadOnlyList<CobranzaPagadorGrupo>>("BuscarClientes",async(cn,user,session,token)=>
        {
            var buscar=(filtro.Buscar??"").Trim();
            if(buscar.Length>100||(buscar.Length is >0 and <2))
                throw new InvalidOperationException("Escribí al menos 2 caracteres del código, nombre o CUIT del cliente (hasta 100).");
            if(filtro.Desde is { } d&&filtro.Hasta is { } h&&d.Date>h.Date)
                throw new InvalidOperationException("La fecha desde no puede ser posterior a la fecha hasta.");
            var unidad=(filtro.Unidad??"").Trim();
            if(unidad.Length>0&&!user.Admin&&unidad!=user.Unidad)
                throw new InvalidOperationException("La unidad de negocio no está autorizada para el usuario.");
            var docs=await Documentos(cn,"",null,token,user,filtro.Hasta?.Date.AddDays(1),buscar,filtro.Desde?.Date,unidad,
                filtro.SoloVencidos?DateTime.Today:null);
            var grupos=docs.Where(d=>d.Moneda=="1"&&!string.IsNullOrWhiteSpace(d.Unidad))
                .GroupBy(d=>(d.Cliente,d.Unidad))
                .Select(g=>new CobranzaPagadorGrupo(g.Key.Cliente,g.First().Nombre,g.Key.Unidad,
                    g.OrderBy(d=>d.Fecha).ThenBy(d=>d.Id).ToList(),[],true))
                .OrderBy(g=>g.Nombre).ThenBy(g=>g.Unidad).ToList();
            VerificarSesion(session);
            return grupos;
        },ct,MenuKey);

    // Comprobantes pendientes en pesos, uno por fila, para el listado de la pantalla Cobranzas. Aplica fechas, unidad y
    // vencidos en SQL; la búsqueda libre por cualquier columna la resuelve la pantalla sobre lo ya cargado.
    public Task<IReadOnlyList<CobranzaDocumento>> PendientesAsync(CobranzaFiltro filtro,CancellationToken ct=default)
        => Ejecutar<IReadOnlyList<CobranzaDocumento>>("BuscarClientes",async(cn,user,session,token)=>
        {
            if(filtro.Desde is { } d&&filtro.Hasta is { } h&&d.Date>h.Date)
                throw new InvalidOperationException("La fecha desde no puede ser posterior a la fecha hasta.");
            var unidad=(filtro.Unidad??"").Trim();
            if(unidad.Length>0&&!user.Admin&&unidad!=user.Unidad)
                throw new InvalidOperationException("La unidad de negocio no está autorizada para el usuario.");
            var docs=await Documentos(cn,"",null,token,user,filtro.Hasta?.Date.AddDays(1),"",filtro.Desde?.Date,unidad,
                filtro.SoloVencidos?DateTime.Today:null);
            var filas=docs.Where(d=>d.Moneda=="1"&&!string.IsNullOrWhiteSpace(d.Unidad)).ToList();
            VerificarSesion(session);
            return filas;
        },ct,MenuKey);

    // Unidades que el usuario puede ver: todas si es administrador central; si no, solo la propia.
    public Task<IReadOnlyList<CobranzaUnidad>> UnidadesAsync(CancellationToken ct=default)
        => Ejecutar<IReadOnlyList<CobranzaUnidad>>("Unidades",async(cn,user,session,token)=>
        {
            var filas=(await cn.QueryAsync<CobranzaUnidad>(Cmd("""
                SELECT LTRIM(RTRIM(Codigo)) Codigo,ISNULL(LTRIM(RTRIM(Descripcion)),'') Descripcion
                FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo))<>'' AND (@admin=1 OR LTRIM(RTRIM(Codigo))=@unidad)
                ORDER BY Codigo;
                """,new{admin=user.Admin,unidad=user.Unidad},null,token))).AsList();
            VerificarSesion(session);
            return filas;
        },ct,MenuKey);

    public Task<CobranzaPreparacion> PrepararDirectaAsync(string cliente,string unidad,CancellationToken ct=default)
        => Ejecutar("PrepararDirecta",async(cn,user,session,token)=>
        {
            cliente=(cliente??"").Trim(); unidad=(unidad??"").Trim();
            if(cliente.Length==0||unidad.Length==0) throw new InvalidOperationException("Seleccioná el cliente y la unidad de negocio.");
            var docs=(await Documentos(cn,cliente,null,token,user)).Where(d=>d.Moneda=="1"&&d.Unidad==unidad).ToList();
            if(docs.Count==0) throw new InvalidOperationException("El cliente no tiene comprobantes pendientes en pesos para esa unidad de negocio.");
            var talonarios=await Talonarios(cn,unidad,null,token);
            if(talonarios.Count==0) throw new InvalidOperationException("No hay talonario de recibos CB habilitado para la unidad de negocio.");
            var medios=await Medios(cn,null,token,incluirEfectivo:true);
            var hoy=DateTime.Today;
            var saldos=await SaldoClienteAsync(cn,cliente,hoy,user,null,token);
            VerificarSesion(session);
            return new CobranzaPreparacion { Fecha=hoy,SaldoAl=hoy,SaldosCliente=saldos,Documentos=docs,Medios=medios,Talonarios=talonarios };
        },ct,MenuKey);
}
