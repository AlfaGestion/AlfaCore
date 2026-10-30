using System.Globalization;
using AlfaCore.Models;
using Dapper;

namespace AlfaCore.Services;

public sealed partial class CobranzasService
{
    public Task<CobranzaPagadores> BuscarPagadoresAsync(long movimientoId,string buscar="",CancellationToken ct=default)
        =>Ejecutar("BuscarPagador",async(cn,user,session,token)=>
        {
            buscar=buscar.Trim();
            if(buscar.Length>100)throw new InvalidOperationException("La búsqueda admite hasta 100 caracteres.");
            var mov=await LeerMovimiento(cn,movimientoId,null,token,exigirCliente:false);
            var valor=await cn.ExecuteScalarAsync<string?>(Cmd("SELECT VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-TOLERANCIA';",null,null,token));
            if(!decimal.TryParse(valor,NumberStyles.Number,CultureInfo.InvariantCulture,out var tolerancia)||tolerancia<0||tolerancia>100)
                throw new InvalidOperationException("Revisá CONCILIACION-TOLERANCIA (0 a 100, con punto decimal).");
            var docs=await Documentos(cn,"",null,token,user,mov.Fecha.Date.AddDays(1),buscar);
            var grupos=new List<CobranzaPagadorGrupo>();
            foreach(var g in docs.Where(d=>d.Moneda=="1"&&!string.IsNullOrWhiteSpace(d.Unidad)).GroupBy(d=>(d.Cliente,d.Unidad)))
            {
                token.ThrowIfCancellationRequested();
                var documentos=g.OrderBy(d=>d.Fecha).ThenBy(d=>d.Id).ToList();
                var (ids,completa)=CobranzaPagadoresCoincidencias.Buscar(documentos,mov.Importe,tolerancia,token);
                grupos.Add(new(g.Key.Cliente,g.First().Nombre,g.Key.Unidad,documentos,ids,completa));
            }
            VerificarSesion(session);
            return new CobranzaPagadores(mov.Importe,mov.Fecha,tolerancia,grupos.OrderByDescending(g=>g.Coincidentes.Count>0)
                .ThenBy(g=>g.Coincidentes.Count>0?Math.Abs(g.TotalCoincidente-mov.Importe):Math.Abs(g.TotalPendiente-mov.Importe))
                .ThenBy(g=>g.Nombre).ThenBy(g=>g.Unidad).ToList());
        },ct);
}

internal static class CobranzaPagadoresCoincidencias
{
    // Revisa todas las facturas para coincidencias individuales. Para combinaciones, busca hasta
    // cinco facturas con un presupuesto por cliente; si se agota, lo informa, sin afirmar ausencia.
    internal static (IReadOnlyList<int> Ids,bool Completa) Buscar(IReadOnlyList<CobranzaDocumento> documentos,decimal importe,decimal tolerancia,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        var exacta=documentos.Where(d=>Math.Abs(d.Saldo-importe)<=tolerancia).OrderBy(d=>Math.Abs(d.Saldo-importe)).FirstOrDefault();
        if(exacta is not null)return ([exacta.Id],true);
        if(documentos.Count is >1 and <=50 && Math.Abs(documentos.Sum(d=>d.Saldo)-importe)<=tolerancia)
            return (documentos.Select(d=>d.Id).ToArray(),true);
        var candidatos=documentos.Where(d=>d.Saldo>0&&d.Saldo<=importe+tolerancia).OrderByDescending(d=>d.Saldo).ThenBy(d=>d.Id).ToList();
        var actual=new List<int>();var pasos=0;var agotado=false;
        List<int>? Buscar(int desde,int faltan,decimal suma)
        {
            ct.ThrowIfCancellationRequested();
            if(++pasos>20000){agotado=true;return null;}
            if(faltan==0)return Math.Abs(suma-importe)<=tolerancia?actual.ToList():null;
            for(var i=desde;i<=candidatos.Count-faltan;i++)
            {
                if(++pasos>20000){agotado=true;return null;}
                var siguiente=suma+candidatos[i].Saldo;
                if(siguiente>importe+tolerancia)continue;
                actual.Add(candidatos[i].Id);var resultado=Buscar(i+1,faltan-1,siguiente);actual.RemoveAt(actual.Count-1);
                if(resultado is not null||agotado)return resultado;
            }
            return null;
        }
        for(var cantidad=2;cantidad<=Math.Min(5,candidatos.Count);cantidad++)
        {
            var resultado=Buscar(0,cantidad,0);
            if(resultado is not null)return (resultado,true);
            if(agotado)return ([],false);
        }
        return ([],true);
    }
}
