using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class ProveedorAltaRapidaService(ISessionService sesiones,IAppUserSessionService usuarios,IPermissionService permisos,
    ICuentasComercialesService cuentas,IArcaPadronService arca,IAppEventService eventos)
{
    public const string PermisoProveedor="D010110";
    private async Task<T> Ejecutar<T>(Guid sesion,int? baseEsperada,string accion,Func<CancellationToken,Task<T>> trabajo,CancellationToken ct)
    {
        var autor=usuarios.GetCurrentUserName();var sistema=usuarios.CurrentUser?.SystemCode;
        void Vigente()
        {
            ct.ThrowIfCancellationRequested();
            if(!TenantDataAccessGuard.TryGetAuthorizedSession(sesiones,usuarios,out var actual)||actual!=sesion||
                baseEsperada is >0&&sesiones.GetActiveSession()?.BaseId!=baseEsperada||usuarios.GetCurrentUserName()!=autor||usuarios.CurrentUser?.SystemCode!=sistema)
                throw new OperationCanceledException("La base o la sesión cambió. Volvé a abrir el alta de proveedor.");
        }
        try
        {
            Vigente();
            if(!await permisos.HasAccessAsync(PermisoProveedor,ct)||!await permisos.HasAccessAsync(CompraCargaService.MenuKey,ct))
                throw new InvalidOperationException("Necesitás permiso de proveedores y de preparación de compras para usar el alta rápida.");
            Vigente();var resultado=await trabajo(ct);Vigente();return resultado;
        }
        catch(OperationCanceledException){throw;}
        catch(AppUserFacingException){throw;}
        catch(AppValidationException){throw;}
        catch(Exception ex)
        {
            var mensaje=ex is InvalidOperationException?ex.Message:"No se pudo completar el alta de proveedor.";
            var codigo=await eventos.LogErrorAsync("CargaCompras",accion,ex,mensaje,ct:ct);throw new AppUserFacingException(mensaje,codigo,ex);
        }
    }
    public Task<CuentaComercialLookupDataDto> CatalogoAsync(Guid sesion,int? baseEsperada,CancellationToken ct=default)
        =>Ejecutar(sesion,baseEsperada,"CatalogoProveedor",t=>cuentas.GetLookupDataAsync(CuentaComercialTipo.Proveedor,t),ct);
    public Task<ArcaPadronPersonaDto> ConsultarArcaAsync(Guid sesion,int? baseEsperada,string cuit,CancellationToken ct=default)
        =>Ejecutar(sesion,baseEsperada,"ConsultarArcaProveedor",t=>arca.ConsultarPersonaAsync(Cuit(cuit),t),ct);
    public Task<IReadOnlyList<CuentaComercialGridItemDto>> DuplicadosAsync(Guid sesion,int? baseEsperada,string cuit,CancellationToken ct=default)
        =>Ejecutar<IReadOnlyList<CuentaComercialGridItemDto>>(sesion,baseEsperada,"BuscarCuitProveedor",async t=>
        {
            var digitos=Cuit(cuit);var encontrados=new List<CuentaComercialGridItemDto>();
            foreach(var termino in new[]{digitos,$"{digitos[..2]}-{digitos[2..10]}-{digitos[10]}"})
            {
                var resultado=await cuentas.SearchAsync(CuentaComercialTipo.Proveedor,new(){Texto=termino,Activo=null,PageSize=200},t);
                encontrados.AddRange(resultado.Items.Where(p=>new string(p.NumeroDocumento.Where(char.IsAsciiDigit).ToArray())==digitos));
            }
            return encontrados.DistinctBy(p=>p.Codigo.Trim()).ToList();
        },ct);
    public Task<string> CrearAsync(Guid sesion,int? baseEsperada,CuentaComercialSaveRequest request,CancellationToken ct=default)
        =>Ejecutar(sesion,baseEsperada,"CrearProveedor",t=>{
            _=Cuit(request.NumeroDocumento);
            if(!string.IsNullOrWhiteSpace(request.Codigo)||!string.IsNullOrWhiteSpace(request.CodigoOriginal))throw new InvalidOperationException("El código se asigna automáticamente al crear el proveedor.");
            if(string.IsNullOrWhiteSpace(request.IvaCodigo)||string.IsNullOrWhiteSpace(request.CondicionComercialCodigo)||string.IsNullOrWhiteSpace(request.MotivoCompraCodigo))
                throw new InvalidOperationException("Seleccioná condición de IVA, condición de compra y motivo habitual antes de guardar.");
            return cuentas.CrearProveedorEnSesionAsync(request,sesion,t);
        },ct);
    private static string Cuit(string valor)
    {
        var digitos=new string(valor.Where(char.IsAsciiDigit).ToArray());
        if(digitos.Length!=11)throw new InvalidOperationException("Ingresá un CUIT de once dígitos.");return digitos;
    }
}
