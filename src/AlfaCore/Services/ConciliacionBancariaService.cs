using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AlfaCore.ConciliacionBancaria.Application.Services;
using AlfaCore.ConciliacionBancaria.Domain.Entities;
using AlfaCore.ConciliacionBancaria.Domain.Enums;
using AlfaCore.ConciliacionBancaria.Infrastructure;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionBancariaService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events, ConciliacionExtractoLector lector)
{
    public const string MenuKey = "D75-CONCILIA-IA";

    private async Task<T> Ejecutar<T>(string accion, Func<SqlConnection,CancellationToken,Task<T>> work, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var session))
            throw new InvalidOperationException("Iniciá sesión en la base donde querés conciliar.");
        var connection = sessions.GetConnectionString();
        try
        {
            if (!await permissions.HasAccessAsync(MenuKey, ct))
                throw new InvalidOperationException("No tenés permiso para usar Conciliación bancaria inteligente.");
            VerificarSesion(session);
            await using var cn = new SqlConnection(connection);
            await cn.OpenAsync(ct);
            var result = await work(cn, ct);
            ct.ThrowIfCancellationRequested();
            VerificarSesion(session);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var message = ex is InvalidOperationException || ex is SqlException { Number: 50001 }
                ? ex.Message : "No se pudo procesar la conciliación bancaria. Conservamos los datos para reintentar.";
            var code = await events.LogErrorAsync("ConciliacionBancaria", accion, ex, message, ct: ct);
            throw new AppUserFacingException(message, code, ex);
        }
    }

    private void VerificarSesion(Guid id)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var current) || current != id)
            throw new OperationCanceledException("La sesión cambió. Volvé a seleccionar el archivo en la base activa.");
    }

    public Task<ConciliacionLectura> LeerAsync(ConciliacionArchivo file, BankSource? bank, CancellationToken ct = default)
        => Ejecutar("LeerArchivo", (_, token) => Task.Run(() => lector.LeerAsync(file,bank,token),token),ct);

    public Task<IReadOnlyList<ConciliacionCuenta>> CuentasAsync(CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<ConciliacionCuenta>>("Cuentas", async (cn,token) =>
            (await cn.QueryAsync<ConciliacionCuenta>(new CommandDefinition("""
                SELECT LTRIM(RTRIM(CODIGO)) Codigo,DESCRIPCION Descripcion,ISNULL(LTRIM(RTRIM(Moneda)),'') Moneda
                FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO))='DB'
                AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0 ORDER BY DESCRIPCION,CODIGO;
                """, cancellationToken: token))).AsList(),ct);

    public Task<PagedResult<ConciliacionImportacion>> ImportacionesAsync(int page, CancellationToken ct = default)
        => Ejecutar("Importaciones", async (cn,token) =>
        {
            using var r = await cn.QueryMultipleAsync(new CommandDefinition("""
                SELECT ImportacionId Id,OrigenCodigo Banco,ArchivoNombre Archivo,ISNULL(CuentaBanco,'') Cuenta,
                    FechaImportacionUtc FechaUtc,UsuarioImportacion Usuario,CantidadMovimientos Movimientos,ImporteTotal Total,EstadoProceso Estado
                FROM dbo.BK_ImportacionExtracto ORDER BY ImportacionId DESC OFFSET @skip ROWS FETCH NEXT 50 ROWS ONLY;
                SELECT COUNT(*) FROM dbo.BK_ImportacionExtracto;
                """,new { skip=checked((Math.Max(1,page)-1)*50) }, cancellationToken: token));
            return new PagedResult<ConciliacionImportacion> { Items=(await r.ReadAsync<ConciliacionImportacion>()).AsList(), Total=await r.ReadSingleAsync<int>(),PageNumber=page,PageSize=50 };
        },ct);

    public Task<PagedResult<ConciliacionMovimiento>> MovimientosAsync(long? import, int page, CancellationToken ct = default)
        => MovimientosAsync(new ConciliacionMovimientoFiltros { ImportacionId=import, Pagina=page },ct);

    public Task<PagedResult<ConciliacionMovimiento>> MovimientosAsync(ConciliacionMovimientoFiltros filtros, CancellationToken ct = default)
        => Ejecutar("Movimientos", (cn,token) => MovimientosCoreAsync(cn,filtros,token),ct);

    internal async Task<PagedResult<ConciliacionMovimiento>> MovimientosCoreAsync(SqlConnection cn,
        ConciliacionMovimientoFiltros filtros,CancellationToken token,SqlTransaction? tx=null)
        {
            var columna = filtros.OrdenPor switch
            {
                "fecha" => "Fecha", "banco" => "Banco", "descripcion" => "Descripcion", "referencia" => "Referencia",
                "importe" => "Importe", "clasificacion" => "Categoria", "cliente" => "Cliente", "propuesta" => "Propuesta",
                "confianza" => "Confianza", "estado" => "Estado", "conciliacion" => "ConciliacionId",
                _ => throw new InvalidOperationException("La columna de orden no está disponible.")
            };
            var clasificacion=filtros.Clasificacion.Trim();
            if (clasificacion.Length>0 && !Enum.GetNames<MovementCategory>().Contains(clasificacion))
                throw new InvalidOperationException("La clasificación seleccionada no está disponible.");
            if (filtros.Pagina<1 || filtros.Buscar.Length>200)
                throw new InvalidOperationException("Revisá la página y el texto de búsqueda (hasta 200 caracteres).");
            var texto=filtros.Buscar.Trim();
            var patron="%"+texto.Replace("[","[[]").Replace("%","[%]").Replace("_","[_]")+"%";
            // El nombre de columna y la dirección provienen exclusivamente de la lista compilada.
            using var r = await cn.QueryMultipleAsync(new CommandDefinition($"""
                SELECT m.MovimientoId Id,m.FechaMovimiento Fecha,m.OrigenCodigo Banco,m.DescripcionOriginal Descripcion,
                    ISNULL(m.ReferenciaExterna,'') Referencia,m.Importe,m.Saldo,
                    ISNULL(c.TipoMovimiento,'NoIdentificado') Categoria,
                    COALESCE(NULLIF(LTRIM(RTRIM(cliente.RAZON_SOCIAL)),''),
                        NULLIF(NULLIF(LTRIM(RTRIM(p.ClienteNombre)),''),'.'),
                        NULLIF(NULLIF(LTRIM(RTRIM(c.ClienteProbableNombre)),''),'.'),NULLIF(LTRIM(RTRIM(p.ClienteCodigo)),''),'') Cliente,
                    ISNULL(p.ResumenPropuesta,'') Propuesta,COALESCE(p.Confianza,c.Confianza,0) Confianza,
                    ISNULL(c.Motivo,'') Observaciones,m.EstadoRevision Estado,v.ConciliacionId,ISNULL(p.TipoMatch,'') TipoMatch,
                    p.ClienteCodigo CodigoCliente,m.MonedaCodigo
                INTO #Revision
                FROM dbo.BK_ExtractoMovimiento m
                LEFT JOIN dbo.BK_ConciliacionBanco v ON v.MovimientoId=m.MovimientoId AND v.Activa=1
                OUTER APPLY(SELECT TOP (1) * FROM dbo.BK_MovimientoClasificado c WHERE c.MovimientoId=m.MovimientoId ORDER BY c.ClasificacionId DESC)c
                OUTER APPLY(SELECT TOP (1) * FROM dbo.BK_MovimientoMatch p WHERE p.MovimientoId=m.MovimientoId
                    AND c.TipoMovimiento='Cobranza' AND p.EstadoPropuesta<>'RECLASIFICADO' ORDER BY p.MatchId DESC)p
                LEFT JOIN dbo.VT_CLIENTES cliente ON LTRIM(RTRIM(cliente.CODIGO))=LTRIM(RTRIM(p.ClienteCodigo))
                WHERE (@import IS NULL OR m.ImportacionId=@import);
                SELECT * INTO #Filtrados FROM #Revision
                WHERE (@clasificacion='' OR Categoria=@clasificacion)
                  AND (@texto='' OR CONCAT(Descripcion,' ',Referencia,' ',Cliente,' ',Propuesta,' ',Banco,' ',Categoria,' ',Estado,' ',
                      CONVERT(varchar(30),Id),' ',CONVERT(char(10),Fecha,103),' ',CONVERT(varchar(40),Importe),' ',
                      REPLACE(CONVERT(varchar(40),Importe),'.',','),' ',CONVERT(varchar(10),Confianza)) LIKE @patron);
                SELECT * INTO #Pagina FROM #Filtrados ORDER BY [{columna}] {(filtros.Descendente ? "DESC" : "ASC")},Id DESC
                OFFSET @skip ROWS FETCH NEXT 50 ROWS ONLY;
                {ConciliacionRepository.ToleranciaCobroSql}
                -- Aviso actual, independiente de la propuesta guardada al importar. Solo se consultan las 50 filas de la página.
                SELECT pagina.*,cobro.ReciboPosible,cobro.FechaPosibleCobro,cobro.ImportePosibleCobro
                FROM #Pagina pagina OUTER APPLY (
                    SELECT TOP(1) CONCAT(LTRIM(RTRIM(a.TC)),' ',a.SUCURSAL,'-',a.NUMERO,' ',a.LETRA) ReciboPosible,
                        a.FECHA FechaPosibleCobro,CONVERT(decimal(18,2),a.IMPORTE) ImportePosibleCobro
                    FROM dbo.MV_ASIENTOS a
                    WHERE pagina.Categoria='Cobranza' AND pagina.Estado<>'Cobrado' AND pagina.Importe>0 AND pagina.MonedaCodigo='ARS'
                        AND LTRIM(RTRIM(a.CUENTA))=LTRIM(RTRIM(pagina.CodigoCliente)) AND a.EsResumen=0
                        AND LTRIM(RTRIM(a.TC)) IN ('CB','CBFP','CBCT') AND a.SECUENCIA=1 AND a.[DEBE-HABER]='H'
                        AND LTRIM(RTRIM(a.MONEDA))='1' AND ABS(a.IMPORTE-pagina.Importe)<=@toleranciaCobro
                        AND a.FECHA>=DATEADD(day,-15,CONVERT(date,pagina.Fecha)) AND a.FECHA<DATEADD(day,16,CONVERT(date,pagina.Fecha))
                        AND EXISTS(SELECT 1 FROM dbo.TA_USUARIOS u WHERE UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@usuario)
                            AND (ISNULL(u.AdminCentral,0)=1 OR LTRIM(RTRIM(u.UNEGOCIO))=LTRIM(RTRIM(a.UNEGOCIO))))
                        AND NOT EXISTS(SELECT 1 FROM dbo.V_MV_CPTE f WHERE f.TC=a.TC AND f.SUCURSAL=a.SUCURSAL
                            AND f.NUMERO=a.NUMERO AND f.LETRA=a.LETRA AND ISNULL(f.ANULADA,0)=1)
                    ORDER BY ABS(a.IMPORTE-pagina.Importe),ABS(DATEDIFF(day,a.FECHA,pagina.Fecha)),a.FECHA DESC,
                        a.PERIODO DESC,a.MES_OPERATIVO DESC,a.[NUMERO ASIENTO] DESC,a.TIPO_REG
                ) cobro
                ORDER BY pagina.[{columna}] {(filtros.Descendente ? "DESC" : "ASC")},pagina.Id DESC;
                SELECT COUNT(*) FROM #Filtrados;
                """,new { import=filtros.ImportacionId,clasificacion,texto,patron,skip=checked((filtros.Pagina-1)*50),usuario=users.GetCurrentUserName() }, transaction:tx,cancellationToken: token));
            return new PagedResult<ConciliacionMovimiento> { Items=(await r.ReadAsync<ConciliacionMovimiento>()).AsList(),Total=await r.ReadSingleAsync<int>(),PageNumber=filtros.Pagina,PageSize=50 };
        }

    public Task<IReadOnlyList<MatchDetailItem>> DetalleAsync(long id, CancellationToken ct = default)
        => Ejecutar<IReadOnlyList<MatchDetailItem>>("Detalle", async(cn,token) =>
            (await cn.QueryAsync<MatchDetailItem>(new CommandDefinition("""
                SELECT p.MatchId,p.TipoMatch MatchType,p.ClienteCodigo ClientCode,
                    COALESCE(NULLIF(LTRIM(RTRIM(cliente.RAZON_SOCIAL)),''),NULLIF(NULLIF(LTRIM(RTRIM(p.ClienteNombre)),''),'.'),p.ClienteCodigo) ClientName,p.Confianza Confidence,
                    p.ResumenPropuesta ProposalSummary,p.EstadoPropuesta Status,a.TipoComprobante DocumentType,a.Letra Letter,
                    a.CentroEmisor EmissionCenter,a.NumeroComprobante DocumentNumber,a.Cuota Installment,
                    a.ImporteAplicado AppliedAmount,a.ImportePendienteComprobante PendingBalance,a.EsParcial IsPartial
                FROM dbo.BK_MovimientoMatch p LEFT JOIN dbo.BK_MovimientoAplicacion a ON a.MatchId=p.MatchId
                LEFT JOIN dbo.VT_CLIENTES cliente ON LTRIM(RTRIM(cliente.CODIGO))=LTRIM(RTRIM(p.ClienteCodigo))
                WHERE p.MovimientoId=@id AND p.EstadoPropuesta<>'RECLASIFICADO' ORDER BY p.MatchId DESC,a.AplicacionId;
                """, new { id },cancellationToken:token))).AsList(),ct);

    public Task<ConciliacionResultado> ImportarAsync(ConciliacionArchivo archivo, BankSource banco, string cuenta, CancellationToken ct = default)
        => Ejecutar("Importar", async(cn,token) =>
        {
            var session=sessions.GetActiveSession()!.Id;
            var user=users.GetCurrentUserName();
            // Siempre se relee el archivo en servidor; no se confía en filas normalizadas de la UI.
            var lectura=await Task.Run(()=>lector.LeerAsync(archivo,banco,token),token);
            if(lectura.RequiereFechaReferencia) throw new InvalidOperationException(lectura.Mensaje);
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            CommandDefinition Cmd(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:token);
            var valido=await cn.ExecuteScalarAsync<bool>(Cmd("""
                SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO))=@cuenta
                    AND LTRIM(RTRIM(MEDIODEPAGO))='DB' AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0) THEN 1 ELSE 0 END);
                """,new { cuenta=cuenta.Trim() }));
            if(!valido) throw new InvalidOperationException("Seleccioná una cuenta bancaria activa del plan de cuentas (medio de pago DB).");
            var hash=Convert.ToHexString(SHA256.HashData(archivo.Contenido));
            var previo=await cn.ExecuteScalarAsync<long?>(Cmd("""
                SELECT TOP (1) ImportacionId FROM dbo.BK_ImportacionExtracto WITH(UPDLOCK,HOLDLOCK)
                WHERE HashArchivo=@hash AND LTRIM(RTRIM(CuentaBanco))=@cuenta;
                """,new { hash,cuenta=cuenta.Trim() }));
            if(previo.HasValue) throw new InvalidOperationException($"Este archivo ya fue importado para esta cuenta: importación #{previo}. Abrila desde Importaciones recientes.");
            var id=await cn.ExecuteScalarAsync<long>(Cmd("""
                INSERT INTO dbo.BK_ImportacionExtracto(OrigenCodigo,ArchivoNombre,ArchivoRuta,HashArchivo,FechaImportacionUtc,UsuarioImportacion,CantidadMovimientos,ImporteTotal,EstadoProceso,CuentaBanco,Observaciones)
                OUTPUT INSERTED.ImportacionId VALUES(@banco,@nombre,'',@hash,SYSUTCDATETIME(),@user,0,0,'PROCESSING',@cuenta,@observaciones);
                """,new { banco=banco.ToString(),nombre=Path.GetFileName(archivo.Nombre),hash,user,cuenta=cuenta.Trim(),
                    observaciones=archivo.FechaReferencia.HasValue ? $"Fecha de referencia confirmada: {archivo.FechaReferencia:yyyy-MM-dd}" : null }));
            int count=0,duplicates=0;
            decimal total=0;
            var occurrences=new Dictionary<string,int>();
            foreach(var m in lectura.Movimientos)
            {
                token.ThrowIfCancellationRequested();
                var baseKey=Huella(cuenta,banco,m);
                occurrences.TryGetValue(baseKey,out var ordinal);
                occurrences[baseKey]=++ordinal;
                // Sin referencia bancaria, conservar multiplicidad de operaciones idénticas.
                var huella=string.IsNullOrWhiteSpace(m.ExternalReference) ? Hash(baseKey+"|"+ordinal) : baseKey;
                var exists=await cn.ExecuteScalarAsync<bool>(Cmd("""
                    SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.BK_ExtractoMovimiento WITH(UPDLOCK,HOLDLOCK) WHERE HuellaWeb=@huella)
                    THEN 1 ELSE 0 END);
                    """,new { huella }));
                if(exists) { duplicates++; continue; }
                await cn.ExecuteAsync(Cmd("""
                    INSERT INTO dbo.BK_ExtractoMovimiento(ImportacionId,OrigenCodigo,FechaMovimiento,FechaValor,ReferenciaExterna,DescripcionOriginal,Importe,Saldo,ContraparteNombre,ContraparteCuit,JsonOrigen,HuellaWeb)
                    VALUES(@id,@banco,@MovementDate,@ValueDate,@ExternalReference,@Description,@Amount,@Balance,@CounterpartyName,@CounterpartyTaxId,@RawDataJson,@huella);
                    """,new { id,banco=banco.ToString(),m.MovementDate,m.ValueDate,m.ExternalReference,m.Description,m.Amount,m.Balance,m.CounterpartyName,m.CounterpartyTaxId,m.RawDataJson,huella }));
                count++; total+=m.Amount;
            }
            if(count==0) throw new InvalidOperationException("Todos los movimientos del archivo ya están importados para esta cuenta.");
            var repo=new ConciliacionRepository(cn,tx);
            await new DeterministicClassificationService(repo).ClassifyImportAsync(id,token);
            var tolerancia=await cn.ExecuteScalarAsync<string?>(Cmd("""
                SELECT COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux))
                FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-TOLERANCIA';
                """));
            if (!decimal.TryParse(tolerancia,NumberStyles.Number,CultureInfo.InvariantCulture,out var margen) || margen<0 || margen>100)
                throw new InvalidOperationException("Revisá CONCILIACION-TOLERANCIA en la configuración: debe ser un número entre 0 y 100 con punto decimal.");
            await new DeterministicMatchingService(repo,margen).GenerateMatchesAsync(id,token);
            await cn.ExecuteAsync(Cmd("""
                UPDATE dbo.BK_ImportacionExtracto SET CantidadMovimientos=@count,ImporteTotal=@total,EstadoProceso='IMPORTED' WHERE ImportacionId=@id;
                INSERT INTO dbo.BK_GeneracionAlfaLog(Accion,Resultado,Mensaje,Usuario) VALUES('IMPORTAR','OK',@mensaje,@user);
                """,new { count,total,id,user,mensaje=$"Importación #{id}: {count} movimientos; {duplicates} duplicados omitidos." }));
            VerificarSesion(session);
            token.ThrowIfCancellationRequested();
            tx.Commit();
            return new ConciliacionResultado(id,count,duplicates,total);
        },ct);

    public static string Huella(string cuenta,BankSource banco,AlfaCore.ConciliacionBancaria.Application.DTOs.NormalizedMovementDto m)
        => Hash(string.Join("|",cuenta.Trim(),banco,m.MovementDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            m.Amount.ToString("0.00",CultureInfo.InvariantCulture),m.ExternalReference.Trim(),
            string.IsNullOrWhiteSpace(m.ExternalReference)?m.Description.Trim():""));
    private static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    public Task<bool> EliminarAsync(long id, CancellationToken ct=default)
        => Ejecutar("EliminarImportacion",async(cn,token)=>
        {
            var session=sessions.GetActiveSession()!.Id;
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            await cn.ExecuteAsync(new CommandDefinition("""
                IF EXISTS(SELECT 1 FROM dbo.BK_ConciliacionBanco r JOIN dbo.BK_ExtractoMovimiento m ON m.MovimientoId=r.MovimientoId WHERE m.ImportacionId=@id)
                    THROW 50001,'La importación tiene historial de conciliación y no puede eliminarse.',1;
                IF EXISTS(SELECT 1 FROM dbo.BK_GeneracionAlfaLog l JOIN dbo.BK_ExtractoMovimiento m ON m.MovimientoId=l.MovimientoId
                    WHERE m.ImportacionId=@id AND l.Accion NOT IN ('IMPORTAR','RECLASIFICAR','ASIGNAR_CLIENTE','REEVALUAR_PROPUESTA'))
                    THROW 50001,'La importación tiene operaciones registradas y no puede eliminarse.',1;
                DELETE a FROM dbo.BK_MovimientoAplicacion a JOIN dbo.BK_MovimientoMatch p ON p.MatchId=a.MatchId
                    JOIN dbo.BK_ExtractoMovimiento m ON m.MovimientoId=p.MovimientoId WHERE m.ImportacionId=@id;
                DELETE p FROM dbo.BK_MovimientoMatch p JOIN dbo.BK_ExtractoMovimiento m ON m.MovimientoId=p.MovimientoId WHERE m.ImportacionId=@id;
                DELETE c FROM dbo.BK_MovimientoClasificado c JOIN dbo.BK_ExtractoMovimiento m ON m.MovimientoId=c.MovimientoId WHERE m.ImportacionId=@id;
                DELETE FROM dbo.BK_ExtractoMovimiento WHERE ImportacionId=@id;
                DELETE FROM dbo.BK_ImportacionExtracto WHERE ImportacionId=@id;
                INSERT INTO dbo.BK_GeneracionAlfaLog(Accion,Resultado,Mensaje,Usuario) VALUES('ELIMINAR_IMPORTACION','OK',@mensaje,@usuario);
                """,new { id,mensaje=$"Importación #{id} eliminada por el usuario.",usuario=users.GetCurrentUserName() },tx,cancellationToken:token));
            VerificarSesion(session);
            token.ThrowIfCancellationRequested();
            tx.Commit();
            return true;
        },ct);
}
