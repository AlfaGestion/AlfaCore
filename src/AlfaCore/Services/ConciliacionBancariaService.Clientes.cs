using System.Data;
using System.Globalization;
using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Application.Services;
using AlfaCore.ConciliacionBancaria.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionBancariaService
{
    public Task<IReadOnlyList<ClientSearchResultDto>> BuscarClientesAsync(string texto,CancellationToken ct=default)
        => Ejecutar<IReadOnlyList<ClientSearchResultDto>>("BuscarClientes",async(cn,token)=>
        {
            texto=texto.Trim();
            if(texto.Length<2) return [];
            if(texto.Length>100) throw new InvalidOperationException("La búsqueda admite hasta 100 caracteres.");
            var buscar="%"+texto.Replace("[","[[]").Replace("%","[%]").Replace("_","[_]")+"%";
            var documento=ContraparteBancaria.NormalizarCuit(texto);
            var patronDocumento=ContraparteBancaria.PatronCuit(documento);
            return (await cn.QueryAsync<ClientSearchResultDto>(new CommandDefinition("""
                SELECT TOP(30) LTRIM(RTRIM(CODIGO)) ClientCode,RAZON_SOCIAL ClientName,NUMERO_DOCUMENTO Cuit
                FROM dbo.VT_CLIENTES WHERE ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0
                    AND (CODIGO LIKE @buscar OR RAZON_SOCIAL LIKE @buscar
                        OR (@documento<>'' AND NUMERO_DOCUMENTO LIKE @patronDocumento))
                ORDER BY RAZON_SOCIAL,CODIGO;
                """,new{buscar,documento,patronDocumento},cancellationToken:token))).AsList();
        },ct);

    public Task<bool> AsignarClienteAsync(long movimientoId,string cliente,bool recordar,string? aliasTexto,CancellationToken ct=default)
        => Ejecutar("AsignarCliente",async(cn,token)=>
        {
            cliente=cliente.Trim();
            if(cliente.Length is <1 or >15) throw new InvalidOperationException("Seleccioná un cliente de la búsqueda.");
            var sesion=sessions.GetActiveSession()!.Id;
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:token);
            var mov=await LeerClienteMovimientoAsync(cn,tx,movimientoId,token);
            var seleccionado=await cn.QuerySingleOrDefaultAsync<ClientSearchResultDto>(Q("""
                SELECT LTRIM(RTRIM(CODIGO)) ClientCode,RAZON_SOCIAL ClientName,NUMERO_DOCUMENTO Cuit FROM dbo.VT_CLIENTES
                WHERE LTRIM(RTRIM(CODIGO))=@cliente AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0;
                """,new{cliente})) ?? throw new InvalidOperationException("El cliente no está disponible o está dado de baja.");
            var alias=recordar ? aliasTexto?.Trim() ?? "" : "";
            if(recordar)
            {
                var normalizado=DeterministicMatchingService.Normalize(alias);
                if(alias.Length is <5 or >200 || !alias.Any(char.IsLetter) ||
                    normalizado is "transferencia recibida" or "pago recibido" or "transferencia" ||
                    !(DeterministicMatchingService.Normalize(mov.Descripcion).Contains(normalizado,StringComparison.Ordinal) ||
                        DeterministicMatchingService.Normalize(mov.Contraparte).Contains(normalizado,StringComparison.Ordinal)))
                    throw new InvalidOperationException("Para recordar, indicá el nombre del pagador tal como aparece en el extracto (5 a 200 caracteres), sin conceptos genéricos.");
                var aliases=(await cn.QueryAsync<ClientAliasDto>(Q("""
                    SELECT a.ClienteCodigo ClientCode,a.AliasTexto AliasText,c.NUMERO_DOCUMENTO ClientTaxId
                    FROM dbo.BK_AliasClienteBanco a WITH(UPDLOCK,HOLDLOCK)
                    LEFT JOIN dbo.VT_CLIENTES c ON LTRIM(RTRIM(c.CODIGO))=LTRIM(RTRIM(a.ClienteCodigo)) WHERE a.Activo=1;
                    """))).Where(a=>DeterministicMatchingService.Normalize(a.AliasText)==normalizado).ToList();
                var cuitSeleccionado=ContraparteBancaria.NormalizarCuit(seleccionado.Cuit);
                if(aliases.Any(a=>!string.Equals(a.ClientCode.Trim(),cliente,StringComparison.OrdinalIgnoreCase) &&
                    (cuitSeleccionado.Length!=11 || ContraparteBancaria.NormalizarCuit(a.ClientTaxId)!=cuitSeleccionado)))
                    throw new InvalidOperationException("Ese nombre bancario ya está asociado a otro cliente. Podés asignarlo solo a este movimiento desmarcando Recordar.");
                if(!aliases.Any(a=>string.Equals(a.ClientCode.Trim(),cliente,StringComparison.OrdinalIgnoreCase)))
                    await cn.ExecuteAsync(Q("INSERT INTO dbo.BK_AliasClienteBanco(ClienteCodigo,AliasTexto) VALUES(@cliente,@alias);",new{cliente,alias}));
            }
            var usuario=users.GetCurrentUserName();
            var mensaje=$"Cliente asignado manualmente: {cliente} · {seleccionado.ClientName}. "+
                (recordar ? $"Recordar nombre bancario: {alias}." : "Solo este movimiento.");
            await cn.ExecuteAsync(Q("""
                INSERT INTO dbo.BK_MovimientoClasificado(MovimientoId,TipoMovimiento,Confianza,ReglaCodigo,ClienteProbableCodigo,ClienteProbableNombre,Motivo)
                VALUES(@movimientoId,'Cobranza',100,'CLIENTE_MANUAL',@cliente,@nombre,@mensaje);
                UPDATE dbo.BK_MovimientoMatch SET EstadoPropuesta='RECLASIFICADO' WHERE MovimientoId=@movimientoId;
                INSERT INTO dbo.BK_GeneracionAlfaLog(MovimientoId,Accion,Resultado,Mensaje,Usuario)
                VALUES(@movimientoId,'ASIGNAR_CLIENTE','OK',@mensaje,@usuario);
                """,new{movimientoId,cliente,nombre=seleccionado.ClientName,mensaje,usuario}));
            await GenerarPropuestaClienteAsync(cn,tx,mov.ImportacionId,movimientoId,token);
            VerificarSesion(sesion);token.ThrowIfCancellationRequested();tx.Commit();return true;
        },ct);

    public Task<bool> ReevaluarPropuestaAsync(long movimientoId,CancellationToken ct=default)
        => Ejecutar("ReevaluarPropuesta",async(cn,token)=>
        {
            var sesion=sessions.GetActiveSession()!.Id;
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            var mov=await LeerClienteMovimientoAsync(cn,tx,movimientoId,token);
            await cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.BK_MovimientoMatch SET EstadoPropuesta='RECLASIFICADO' WHERE MovimientoId=@movimientoId;",new{movimientoId},tx,cancellationToken:token));
            await GenerarPropuestaClienteAsync(cn,tx,mov.ImportacionId,movimientoId,token);
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.BK_GeneracionAlfaLog(MovimientoId,Accion,Resultado,Mensaje,Usuario)
                VALUES(@movimientoId,'REEVALUAR_PROPUESTA','OK',N'Propuesta recalculada con datos actuales.',@usuario);
                """,new{movimientoId,usuario=users.GetCurrentUserName()},tx,cancellationToken:token));
            VerificarSesion(sesion);token.ThrowIfCancellationRequested();tx.Commit();return true;
        },ct);

    private async Task<ClienteMovimiento> LeerClienteMovimientoAsync(SqlConnection cn,SqlTransaction tx,long movimientoId,CancellationToken token)
    {
        CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:token);
        if(!await cn.ExecuteScalarAsync<bool>(Q("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.TA_USUARIOS
                WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@usuario)) THEN 1 ELSE 0 END);
            """,new{usuario=users.GetCurrentUserName()})))
            throw new InvalidOperationException("El usuario actual no está definido en la base activa.");
        var mov=await cn.QuerySingleOrDefaultAsync<ClienteMovimiento>(Q("""
            SELECT m.ImportacionId,m.EstadoRevision Estado,m.Importe,m.DescripcionOriginal Descripcion,
                ISNULL(m.ContraparteNombre,'') Contraparte,ISNULL(c.TipoMovimiento,'') Categoria
            FROM dbo.BK_ExtractoMovimiento m WITH(UPDLOCK,HOLDLOCK)
            OUTER APPLY(SELECT TOP 1 TipoMovimiento FROM dbo.BK_MovimientoClasificado
                WHERE MovimientoId=m.MovimientoId ORDER BY ClasificacionId DESC)c
            WHERE m.MovimientoId=@movimientoId;
            """,new{movimientoId})) ?? throw new InvalidOperationException("El movimiento ya no está disponible.");
        if(mov.Estado=="Cobrado" || await cn.ExecuteScalarAsync<bool>(Q("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_ConciliacionBanco WHERE MovimientoId=@movimientoId AND Activa=1)
                OR EXISTS(SELECT 1 FROM dbo.ALFACORE_COBRANZA_OPERACION WHERE MovimientoId=@movimientoId)
                OR EXISTS(SELECT 1 FROM dbo.BK_GeneracionAlfaLog WHERE MovimientoId=@movimientoId AND Accion='COBRANZA' AND Resultado='OK')
                THEN 1 ELSE 0 END);
            """,new{movimientoId})))
            throw new InvalidOperationException("El movimiento ya está cobrado o conciliado. Su cliente no se puede cambiar desde la revisión.");
        if(mov.Importe<=0 || mov.Categoria!="Cobranza")
            throw new InvalidOperationException("Asigná el cliente a un ingreso clasificado como Cobranza.");
        return mov;
    }

    private static async Task GenerarPropuestaClienteAsync(SqlConnection cn,SqlTransaction tx,long importacionId,long movimientoId,CancellationToken token)
    {
        CommandDefinition Q(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:token);
        var valor=await cn.ExecuteScalarAsync<string?>(Q("SELECT COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux)) FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-TOLERANCIA';"));
        if(!decimal.TryParse(valor,NumberStyles.Number,CultureInfo.InvariantCulture,out var tolerancia)||tolerancia<0||tolerancia>100)
            throw new InvalidOperationException("Revisá CONCILIACION-TOLERANCIA en la configuración.");
        await new DeterministicMatchingService(new ConciliacionRepository(cn,tx,movimientoId),tolerancia).GenerateMatchesAsync(importacionId,token);
    }

    private sealed class ClienteMovimiento
    {
        public long ImportacionId { get; set; }
        public string Estado { get; set; }="";
        public string Categoria { get; set; }="";
        public decimal Importe { get; set; }
        public string Descripcion { get; set; }="";
        public string Contraparte { get; set; }="";
    }
}
