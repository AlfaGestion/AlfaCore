using System.Globalization;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class CompraCargaService(ISessionService sesiones,IAppUserSessionService usuarios,IPermissionService permisos,
    IAppEventService eventos,IInterfacesService documentos,IArticulosService? articulosMaestro=null)
{
    public const string MenuKey="D50-CARGA-COMPRAS-WEB";
    private async Task<T> Ejecutar<T>(int? baseEsperada,string accion,Func<SqlConnection,CancellationToken,Task<T>> trabajo,CancellationToken ct,bool registrarErrores=true)
    {
        if(!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones,usuarios,out var sesion)||
            baseEsperada is >0&&sesiones.GetActiveSession()?.BaseId!=baseEsperada)
            throw new InvalidOperationException("Iniciá sesión en la base donde querés preparar la compra.");
        var usuario=usuarios.GetCurrentUserName();var sistema=usuarios.CurrentUser?.SystemCode;
        void Vigente(){if(!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones,usuarios,out var actual)||actual!=sesion||usuarios.GetCurrentUserName()!=usuario||usuarios.CurrentUser?.SystemCode!=sistema)throw new OperationCanceledException("La sesión o base activa cambió.");}
        try
        {
            if(!await permisos.HasAccessAsync(MenuKey,ct))throw new InvalidOperationException("No tenés permiso para preparar compras.");
            Vigente();await using var cn=new SqlConnection(sesiones.GetConnectionString());await cn.OpenAsync(ct);
            var resultado=await trabajo(cn,ct);ct.ThrowIfCancellationRequested();Vigente();return resultado;
        }
        catch(OperationCanceledException){throw;}
        catch(AppUserFacingException){throw;}
        catch(Exception ex)
        {
            var mensaje=ex is InvalidOperationException?ex.Message:"No se pudo preparar la compra. Conservá el documento y reintentá.";
            // El recálculo automático mientras se tipea no debe llenar AUX_ERR con datos todavía incompletos.
            var codigo=registrarErrores||ex is not InvalidOperationException?await eventos.LogErrorAsync("CargaCompras",accion,ex,mensaje,ct:ct):"";
            throw new AppUserFacingException(mensaje,codigo,ex);
        }
    }
    private static CommandDefinition Cmd(string sql,object? args,CancellationToken ct,SqlTransaction? tx=null)=>new(sql,args,tx,commandTimeout:45,cancellationToken:ct);
    private sealed class UsuarioCompra {public string Unidad{get;set;}="";public bool Admin{get;set;}}
    public Task<CompraCargaContexto> ContextoAsync(int? baseEsperada=null,CancellationToken ct=default)=>Ejecutar(baseEsperada,"Contexto",(c,t)=>Contexto(c,t),ct);
    private async Task<CompraCargaContexto> Contexto(SqlConnection c,CancellationToken t,SqlTransaction? tx=null)
    {
        var usuario=await c.QuerySingleOrDefaultAsync<UsuarioCompra>(Cmd("""
            SELECT LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad,CONVERT(bit,ISNULL(AdminCentral,0)) Admin
            FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@usuario)
              AND (@sistema='' OR UPPER(LTRIM(RTRIM(SISTEMA)))=UPPER(@sistema)) AND ISNULL(Activo,1)=1
            """,new{usuario=usuarios.GetCurrentUserName(),sistema=usuarios.CurrentUser?.SystemCode??""},t,tx))
            ??throw new InvalidOperationException("El usuario no está activo en esta base.");
        using var datos=await c.QueryMultipleAsync(Cmd("""
            SELECT LTRIM(RTRIM(CODIGO)) Codigo,DESCRIPCION Descripcion FROM dbo.V_TA_UNIDADNEGOCIO
                WHERE @admin=1 OR LTRIM(RTRIM(CODIGO))=@unidad ORDER BY CODIGO;
            SELECT LTRIM(RTRIM(IdMotivoCpra)) Codigo,Descripcion FROM dbo.V_TA_MOTIVOCPRA ORDER BY IdMotivoCpra;
            SELECT LTRIM(RTRIM(c.CODIGO)) Codigo,c.DESCRIPCION Descripcion,CONVERT(bit,ISNULL(c.usaCCostos,0)) RequiereCentroCosto FROM dbo.MA_CUENTAS c
                WHERE ISNULL(c.TITULO,0)=0 AND ISNULL(c.Dada_De_Baja,0)=0 AND ISNULL(c.BLOQUEO,0)=0 AND LTRIM(RTRIM(ISNULL(c.Moneda,''))) IN ('','1')
                  AND LTRIM(RTRIM(ISNULL(c.MedioDePago,''))) IN ('','RI','RB','RG','RO')
                  AND NOT EXISTS(SELECT 1 FROM dbo.VT_CLIENTES x WHERE x.CODIGO=c.CODIGO)
                  AND NOT EXISTS(SELECT 1 FROM dbo.VT_PROVEEDORES x WHERE x.CODIGO=c.CODIGO) ORDER BY c.DESCRIPCION,c.CODIGO;
            SELECT LTRIM(RTRIM(IdMotivoCpra)) Motivo,LTRIM(RTRIM(CondIva)) CondicionIva,
                LTRIM(RTRIM(ISNULL(C_CFI_CTACPRA,''))) Compra,LTRIM(RTRIM(ISNULL(CFI_IVARI,''))) Iva,
                LTRIM(RTRIM(ISNULL(C_CFI_PERCIVA,''))) PercepcionIva,LTRIM(RTRIM(ISNULL(C_CFI_PERCIB,''))) PercepcionIibb,
                LTRIM(RTRIM(ISNULL(C_CFI_PERCGAN,''))) PercepcionGanancias,LTRIM(RTRIM(ISNULL(C_CFI_IMPINT,''))) ImpuestoInterno FROM dbo.V_TA_INPUT_COMPRAS;
            SELECT LTRIM(RTRIM(CLAVE)) Clave,LTRIM(RTRIM(ISNULL(VALOR,''))) Valor FROM dbo.TA_CONFIGURACION
                WHERE CLAVE IN ('PIVA','PIVADIF','PIVADIF1','PIVADIF2','FECHAOPERDESDE','FECHAOPERHASTA','CIERRECOMPRAS','MotivoComprasDefault','CuentaImpuestoInterno');
            SELECT LTRIM(RTRIM(ISNULL(CFI_IVARI,''))) Iva, LTRIM(RTRIM(ISNULL(CFI_IVARI2,''))) IvaConsumidor,
                LTRIM(RTRIM(ISNULL(CFI_IVARI3,''))) IvaMono,
                LTRIM(RTRIM(ISNULL(C_CFI_PERCIVA,''))) PercepcionIva,LTRIM(RTRIM(ISNULL(C_CFI_PERCIB,''))) PercepcionIibb,
                LTRIM(RTRIM(ISNULL(C_CFI_PERCGAN,''))) PercepcionGanancias,LTRIM(RTRIM(ISNULL(C_CFI_IMPINT,''))) ImpuestoInterno FROM dbo.TA_CUENTASIVA;
            SELECT LTRIM(RTRIM(CODIGO)) Codigo,DESCRIPCION Descripcion,LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad,
                ISNULL(Talonario,0) Talonario,LTRIM(RTRIM(ISNULL(LETRAS,''))) Letras,CONVERT(bit,ISNULL(Externo,0)) Externo,
                LTRIM(RTRIM(ISNULL(ES,''))) Stock,LTRIM(RTRIM(ISNULL(DEBEHABER,''))) DebeHaber
            FROM dbo.V_TA_CPTE WHERE UPPER(LTRIM(RTRIM(SISTEMA)))='COMPRAS'
                AND (@admin=1 OR LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) IN ('',@unidad)) ORDER BY CODIGO,UNEGOCIO,Talonario;
            SELECT LTRIM(RTRIM(CODIGO)) Codigo,DESCRIPCION Descripcion FROM dbo.TA_CCOSTO WHERE ISNULL(TITULO,0)=0 ORDER BY DESCRIPCION;
            DECLARE @dep nvarchar(max)=N'SELECT LTRIM(RTRIM(IdDeposito)) Codigo,Descripcion FROM dbo.V_TA_DEPOSITO';
            IF COL_LENGTH('dbo.V_TA_DEPOSITO','UnidadNegocio') IS NOT NULL
                SET @dep=@dep+N' WHERE @admin=1 OR LTRIM(RTRIM(ISNULL(UnidadNegocio,SPACE(0)))) IN (SPACE(0),@unidad)';
            EXEC sys.sp_executesql @dep,N'@admin bit,@unidad nvarchar(4)',@admin,@unidad;
            """,new{admin=usuario.Admin,unidad=usuario.Unidad},t,tx));
        var unidades=(await datos.ReadAsync<CompraCargaOpcion>()).AsList();var motivos=(await datos.ReadAsync<CompraCargaOpcion>()).AsList();
        var cuentas=(await datos.ReadAsync<CompraCargaOpcion>()).AsList();var reglas=(await datos.ReadAsync<CompraCargaRegla>()).AsList();
        var config=(await datos.ReadAsync<ConfiguracionCompra>()).ToDictionary(x=>x.Clave,x=>x.Valor,StringComparer.OrdinalIgnoreCase);
        var defaults=(await datos.ReadAsync<DefaultFiscal>()).ToList();
        var tipos=(await datos.ReadAsync<CompraCargaTipo>()).AsList();var centros=(await datos.ReadAsync<CompraCargaOpcion>()).AsList();var depositos=(await datos.ReadAsync<CompraCargaOpcion>()).AsList();
        if(tipos.GroupBy(x=>x.Clave).Any(g=>g.Count()>1))throw new InvalidOperationException("Hay comprobantes de compras repetidos para el mismo talonario y unidad. Revisá su configuración.");
        if(defaults.Count>1)throw new InvalidOperationException("Hay más de una configuración en TA_CUENTASIVA; revisala antes de preparar compras.");
        if(reglas.GroupBy(r=>(r.Motivo,r.CondicionIva)).Any(g=>g.Count()>1))throw new InvalidOperationException("Hay imputaciones de compras repetidas para un motivo y condición de IVA.");
        if(defaults.FirstOrDefault() is {} df)
            foreach(var r in reglas)
            {
                if(r.Iva.Length==0)r.Iva=r.CondicionIva switch{"3" or "6"=>df.IvaConsumidor.Length>0?df.IvaConsumidor:df.Iva,"5"=>df.IvaMono.Length>0?df.IvaMono:df.Iva,_=>df.Iva};
                if(r.PercepcionIva.Length==0)r.PercepcionIva=df.PercepcionIva;
                if(r.PercepcionIibb.Length==0)r.PercepcionIibb=df.PercepcionIibb;
                if(r.PercepcionGanancias.Length==0)r.PercepcionGanancias=df.PercepcionGanancias;
                if(r.ImpuestoInterno.Length==0)r.ImpuestoInterno=df.ImpuestoInterno.Length>0?df.ImpuestoInterno:config.GetValueOrDefault("CuentaImpuestoInterno","");
            }
        var tasas=new List<decimal>();var fiscales=new List<decimal>();
        foreach(var clave in new[]{"PIVA","PIVADIF","PIVADIF1","PIVADIF2"})
        {
            var fiscal=0m;
            if(config.TryGetValue(clave,out var valor)&&valor.Length>0)
            {
                if(!decimal.TryParse(valor.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var tasa)||tasa is <0 or >100)
                    throw new InvalidOperationException($"Revisá la alícuota configurada en {clave}.");
                if(tasa>0)tasas.Add(tasa);fiscal=tasa;
            }
            fiscales.Add(fiscal);
        }
        DateTime? Fecha(string clave)
        {
            var v=config.GetValueOrDefault(clave,"");if(v.Length==0)return null;
            if(!DateTime.TryParseExact(v,new[]{"d/M/yyyy","dd/MM/yyyy","yyyy-MM-dd","yyyyMMdd"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var f))
                throw new InvalidOperationException($"Revisá la fecha configurada en {clave}.");
            return f;
        }
        return new CompraCargaContexto(unidades,motivos,cuentas,tasas.Distinct().ToList(),reglas,usuario.Unidad,config.GetValueOrDefault("MotivoComprasDefault",""),Fecha("FECHAOPERDESDE"),Fecha("FECHAOPERHASTA"),Fecha("CIERRECOMPRAS")){Tipos=tipos,TasasFiscales=fiscales,CentrosCosto=centros,Depositos=depositos};
    }
    private sealed class ConfiguracionCompra{public string Clave{get;set;}="";public string Valor{get;set;}="";}
    private sealed class DefaultFiscal:CompraFiscalBase{public string IvaConsumidor{get;set;}="";public string IvaMono{get;set;}="";}
    private class CompraFiscalBase{public string ImpuestoInterno{get;set;}="";public string Iva{get;set;}="";public string PercepcionIva{get;set;}="";public string PercepcionIibb{get;set;}="";public string PercepcionGanancias{get;set;}="";}
    // Cada palabra escrita debe aparecer en código, razón social o documento (en cualquier orden).
    // El orden prioriza código exacto, código que empieza igual, razón social que empieza igual y palabra que empieza igual.
    private static string CoincidePalabra(int n)=>$"(LTRIM(RTRIM(CODIGO)) LIKE @t{n} ESCAPE '\\' OR RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @t{n} ESCAPE '\\' OR NUMERO_DOCUMENTO LIKE @t{n} ESCAPE '\\')";
    private static readonly string ProveedorSql=$"""
        SELECT TOP(30) LTRIM(RTRIM(CODIGO)) Codigo,RAZON_SOCIAL Nombre,ISNULL(NUMERO_DOCUMENTO,'') Documento,
            LTRIM(RTRIM(ISNULL(IVA,''))) CondicionIva,LTRIM(RTRIM(ISNULL(IdMotivoCpra,''))) Motivo,
            LTRIM(RTRIM(ISNULL(CodigoImputacion,''))) Imputacion,CONVERT(bit,ISNULL(ExentoIvaServicios,0)) ExentoServicios,
            CONVERT(bit,ISNULL(ExentoIVAOtros,0)) ExentoOtros,CONVERT(bit,ISNULL(ExentoIvaArticulos,0)) ExentoArticulos,CONVERT(bit,ISNULL(FCC_Sin_OC,0)) SinOc,LTRIM(RTRIM(ISNULL(Moneda,''))) Moneda
        FROM dbo.VT_PROVEEDORES WHERE ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(BLOQUEO,0)=0
          AND ((@codigo<>'' AND LTRIM(RTRIM(CODIGO))=@codigo) OR (@codigo='' AND
               (@texto='' OR ({CoincidePalabra(1)} AND {CoincidePalabra(2)} AND {CoincidePalabra(3)} AND {CoincidePalabra(4)})
                OR (@esDocumento=1 AND REPLACE(REPLACE(REPLACE(NUMERO_DOCUMENTO,'-',''),'.',''),' ','') LIKE @documento))))
          ORDER BY CASE WHEN LTRIM(RTRIM(CODIGO))=@texto OR (@esDocumento=1 AND REPLACE(REPLACE(REPLACE(NUMERO_DOCUMENTO,'-',''),'.',''),' ','')=@digitos) THEN 0
                        WHEN LTRIM(RTRIM(CODIGO)) LIKE @prefijo ESCAPE '\' THEN 1
                        WHEN RAZON_SOCIAL COLLATE Latin1_General_CI_AI LIKE @prefijo ESCAPE '\' THEN 2
                        WHEN (' '+RAZON_SOCIAL) COLLATE Latin1_General_CI_AI LIKE @palabra ESCAPE '\' THEN 3 ELSE 4 END,RAZON_SOCIAL,CODIGO
        """;
    private static object ParametrosProveedor(string codigo,string texto)
    {
        var busqueda=texto.Trim();var digitos=new string(busqueda.Where(char.IsAsciiDigit).ToArray());
        static string Literal(string v)=>v.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_").Replace("[","\\[");
        var palabras=busqueda.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(4).Select(Literal).ToArray();
        string T(int i)=>i<palabras.Length?"%"+palabras[i]+"%":"%";
        return new{codigo=codigo.Trim(),texto=busqueda,t1=T(0),t2=T(1),t3=T(2),t4=T(3),prefijo=Literal(busqueda)+"%",palabra="% "+Literal(busqueda)+"%",
            digitos,documento="%"+digitos+"%",esDocumento=digitos.Length>=6&&busqueda.All(c=>char.IsAsciiDigit(c)||c is '-' or '.' or ' ')};
    }
    public Task<IReadOnlyList<CompraCargaProveedor>> BuscarProveedoresAsync(string texto,int? baseEsperada=null,CancellationToken ct=default)=>Ejecutar<IReadOnlyList<CompraCargaProveedor>>(baseEsperada,"BuscarProveedores",async(c,t)=>
    {
        if(texto.Length>100)throw new InvalidOperationException("La búsqueda admite hasta 100 caracteres.");
        return (await c.QueryAsync<CompraCargaProveedor>(Cmd(ProveedorSql,ParametrosProveedor("",texto),t))).AsList();
    },ct);
    public Task<CompraCargaProveedor> ProveedorAsync(string codigo,int? baseEsperada=null,CancellationToken ct=default)=>Ejecutar(baseEsperada,"Proveedor",(c,t)=>Proveedor(c,codigo,t),ct);
    private static async Task<CompraCargaProveedor> Proveedor(SqlConnection c,string codigo,CancellationToken t,SqlTransaction? tx=null)=>
        (await c.QueryAsync<CompraCargaProveedor>(Cmd(ProveedorSql,ParametrosProveedor(codigo,""),t,tx))).SingleOrDefault()
        ??throw new InvalidOperationException("Seleccioná un proveedor activo y habilitado de esta base.");
    public Task<CompraCargaOrigen> OrigenAsync(long id,int? baseEsperada=null,CancellationToken ct=default)=>Ejecutar(baseEsperada,"DocumentoOrigen",async(c,t)=>
    {
        if(!await permisos.HasAccessAsync("D016004",t))throw new InvalidOperationException("No tenés permiso para consultar Recepción documental.");
        var documento=await documentos.GetByIdAsync(id,baseEsperada,t)??throw new InvalidOperationException("No se encontró el documento recibido.");
        if(documento.CompraRegistradaId is not null||documento.Eliminado||documento.EstadoCodigo is "ANULADO" or "RECHAZADO" or "REGISTRADO")throw new InvalidOperationException("El documento recibido está anulado o rechazado.");
        var lectura=documento.LecturaCompra??throw new InvalidOperationException("Primero procesá la lectura del documento en Recepción documental.");
        var avisos=new List<string>();var borrador=CompraCargaOrigenLectura.Convertir(lectura,id,avisos);
        var renglones=await AplicarRenglonesLeidosAsync(c,lectura,borrador,avisos,t);
        // Aviso siempre: si ese comprobante ya está cargado (importado de ARCA o por otra vía) se informa antes de que el operador siga.
        var existente=await BuscarExistenteAsync(c,borrador.Proveedor,borrador.Tipo,borrador.Letra,borrador.Sucursal,borrador.Numero,t);
        if(existente is {Anulada:false} ex)
        {
            if(ex.ImportadaArca){borrador.ReemplazaImportadaId=ex.Id;avisos.Insert(0,$"Este comprobante ya fue cargado por la importación de ARCA ({ex.Fecha:dd/MM/yyyy}, total {ex.Total:N2}). Al registrar se reemplaza por esta carga con su detalle; el total, el IVA y el neto gravado tienen que coincidir con ARCA.");}
            else avisos.Insert(0,$"Este comprobante ya está registrado ({ex.Tipo} {ex.Comprobante}, {ex.Fecha:dd/MM/yyyy}, total {ex.Total:N2}, usuario {ex.Usuario}). No se puede cargar de nuevo.");
        }
        return new CompraCargaOrigen(documento,borrador,avisos,renglones,existente);
    },ct);
    public Task<CompraCargaVista> PrepararAsync(CompraCargaBorrador d,int? baseEsperada=null,CancellationToken ct=default,bool silencioso=false)=>Ejecutar(baseEsperada,"VistaPrevia",async(c,t)=>
    {
        var contexto=await Contexto(c,t);var proveedor=await Proveedor(c,d.Proveedor,t);
        var tipoNumeracion=contexto.Tipos.FirstOrDefault(x=>x.Codigo==d.Tipo&&x.Talonario==d.Talonario&&x.Unidad==d.Unidad)
            ??contexto.Tipos.FirstOrDefault(x=>x.Codigo==d.Tipo&&x.Talonario==d.Talonario&&x.Unidad.Length==0);
        if(tipoNumeracion is {Externo:false,VistaPreviaDisponible:true})
        {var numero=await NumeroInternoAsync(c,d,tipoNumeracion,t);d.Sucursal=numero.Sucursal;d.Numero=numero.Numero;}
        if(d.CompraOrigenId is {} compraOrigen)
        {
            var origen=await LeerOrigenMercaderiaAsync(c,compraOrigen,t);
            if(origen.Huella!=d.CompraOrigenHuella)throw new InvalidOperationException("El remito/OC de origen cambió. Volvé a preparar la carga desde su consulta.");
            ValidarCantidadesOrigen(d,origen.Filas);
        }
        await ValidarArticulosDirectosAsync(c,d,contexto,t);
        var vista=CompraCargaPlanificador.Preparar(d,proveedor,contexto);var errores=vista.Errores.ToList();var avisos=vista.Avisos.ToList();
        if(d.DocumentoRecibido is {} id)
        {
            if(!await permisos.HasAccessAsync("D016004",t))throw new InvalidOperationException("No tenés permiso para consultar el documento origen.");
            var doc=await documentos.GetByIdAsync(id,baseEsperada,t);
            if(doc is null||doc.CompraRegistradaId is not null||doc.Eliminado||doc.EstadoCodigo is "ANULADO" or "RECHAZADO" or "REGISTRADO"||doc.LecturaCompra?.Estado is "APROBADO" or "ELIMINADO" or "RECHAZADO")errores.Add("El documento origen ya no está disponible para una nueva compra.");
        }
        using var r=await c.QueryMultipleAsync(Cmd("""
            SELECT COUNT(*) FROM dbo.C_MV_CPTE WHERE LTRIM(RTRIM(CUENTA))=@proveedor AND LTRIM(RTRIM(TC))=@tc
                AND LTRIM(RTRIM(IDCOMPROBANTE))=@id AND ISNULL(ANULADA,0)=0 AND UPPER(LTRIM(RTRIM(ISNULL(USUARIO,''))))<>'IMPORTACION';
            SELECT COUNT(*) FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))=@tc AND SUCURSAL=@suc AND NUMERO=@nro AND LETRA=@letra;
            """,new{tc=d.Tipo,proveedor=proveedor.Codigo,id=vista.IdComprobante,suc=vista.IdComprobante[..4],nro=vista.IdComprobante.Substring(4,8),letra=d.Letra},t));
        if(await r.ReadSingleAsync<int>()>0)errores.Add("La factura ya existe como compra operativa para este proveedor. No prepares una nueva registración.");
        if(await r.ReadSingleAsync<int>()>0)avisos.Add("Hay renglones contables con ese tipo y número. Debe verificarse su proveedor y origen, incluida una posible importación ARCA, antes de habilitar cualquier grabación.");
        var huella=HuellaRevision(d,proveedor,contexto,vista);
        // Si la vista ya trae errores, ValidarRegistro los repetiría concatenados.
        if(errores.Count==0)
            try{ValidarRegistro(d,proveedor,contexto,vista,huella);}
            catch(InvalidOperationException ex){errores.Add(ex.Message);}
        return vista with{Errores=errores.Distinct().ToList(),Avisos=avisos,Huella=huella};
    },ct,!silencioso);
}
