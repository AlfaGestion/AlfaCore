using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class InterfacesService
{
    private static async Task ValidarAltaTarjetas(SqlConnection c,InterfacesCrearComprobanteRequest request,CancellationToken ct)
    {
        var tipo=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT Codigo FROM dbo.INT_TIPO_DOCUMENTO WHERE IdTipoDocumento=@IdTipoDocumento",request,cancellationToken:ct));
        if(tipo!=TarjetasRecepcionService.Tipo)return;
        if(request.Adjuntos.Count!=1||!Path.GetExtension(request.Adjuntos[0].NombreArchivo).Equals(".pdf",StringComparison.OrdinalIgnoreCase)
            ||request.Adjuntos[0].TamanoBytes is <5 or >20971520)
            throw new InvalidOperationException("Para liquidaciones de tarjetas, adjuntá un PDF de hasta 20 MB por recepción. Podés usar Alta por lote para varios PDFs.");
    }
    private async Task IntentarEncolarTarjetas(long documento,int tipo,Guid? sesionEsperada,CancellationToken ct)
    {
        try
        {
            if(sessionService.GetActiveSession()?.Id!=sesionEsperada)return;
            await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(ct);
            var codigo=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT Codigo FROM dbo.INT_TIPO_DOCUMENTO WHERE IdTipoDocumento=@tipo",new{tipo},cancellationToken:ct));
            if(codigo==TarjetasRecepcionService.Tipo)await EncolarTarjetasAsync(documento,ct,sesionEsperada);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        catch(Exception ex)
        {
            using var cierre=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try{await _appEvents.LogErrorAsync("Interfaces","EncolarTarjetas",ex,"El documento se guardó, pero no se pudo encolar. Abrilo y usá Preparar análisis de tarjetas.",ct:cierre.Token);}
            catch(Exception logError){System.Diagnostics.Debug.WriteLine(logError);}
        }
    }
    public Task EncolarTarjetasAsync(long documento,CancellationToken ct=default,Guid? sesionEsperada=null)
        =>ExecuteLoggedAsync("Interfaces","EncolarTarjetas",async token=>
        {
            if(tarjetasRecepcion is null)throw new InvalidOperationException("La recepción de tarjetas no está disponible en esta instalación.");
            var origen=sessionService.GetActiveSession();
            if(origen is null||(sesionEsperada.HasValue&&origen.Id!=sesionEsperada))throw new OperationCanceledException();
            sesionEsperada=origen.Id;
            await tarjetasRecepcion.VerificarAccesoAsync(token);
            var detalle=await GetByIdAsync(documento,origen.BaseId,token)??throw new InvalidOperationException("No se encontró la recepción.");
            if(detalle.Eliminado||detalle.TipoDocumentoCodigo!=TarjetasRecepcionService.Tipo)
                throw new InvalidOperationException("Elegí una recepción activa de tipo Liquidación de tarjetas.");
            var adjuntos=detalle.Adjuntos.Where(a=>!a.Eliminado).ToList();
            if(adjuntos.Count!=1)throw new InvalidOperationException("La recepción debe tener un único PDF de liquidación.");
            var settings=await interfacesConfigService.GetUploadSettingsAsync(origen.BaseId,token);
            if(!settings.UsaFtp)settings.RutaBase=detalle.RutaBase;
            var temp=Path.Combine(Path.GetTempPath(),"alfacore-tarjeta-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var path=await StageAttachmentForIaAsync(settings,adjuntos[0],temp,token);
                if(new FileInfo(path).Length>20971520)throw new InvalidOperationException("La liquidación supera los 20 MB.");
                await tarjetasRecepcion.EncolarAsync(documento,new(adjuntos[0].NombreOriginal,await File.ReadAllBytesAsync(path,token)),token,sesionEsperada);
            }
            finally{Directory.Delete(temp,true);}
            return true;
        },"No se pudo preparar la liquidación recibida.",ct);
}
