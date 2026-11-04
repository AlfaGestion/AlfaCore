using AlfaCore.Models;

namespace AlfaCore.Services;

internal static class CompraCargaOrigenLectura
{
    internal static string Huella(string json)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
    internal static CompraCargaBorrador Convertir(InterfacesCompraIaResultadoDto ia,long documento,List<string> avisos)
    {
        if(ia.Estado is "PENDIENTE_LECTURA" or "PROCESANDO_LECTURA" or "ERROR_LECTURA" or "CANCELADO" or "ELIMINADO" or "RECHAZADO" or "APROBADO")
            throw new InvalidOperationException("La lectura no está disponible para preparar una nueva compra. Revisá su estado en Recepción documental.");
        if(string.IsNullOrWhiteSpace(ia.JsonResultado))throw new InvalidOperationException("El documento todavía no tiene una lectura guardada. Procesalo desde Recepción documental y volvé a abrirlo.");
        var tipo=ia.TipoComprobante.Trim().ToUpperInvariant();
        var tc=tipo switch
        {
            "FCC" or "FC" or "FACTURA" or "FACTURA A" or "FACTURA C" or "1" or "001" or "11" or "011"=>"FCC",
            "NCC" or "NC" or "NOTA DE CREDITO" or "NOTA DE CRÉDITO" or "3" or "003" or "13" or "013"=>"NCC",
            "NDC" or "ND" or "NOTA DE DEBITO" or "NOTA DE DÉBITO" or "2" or "002" or "12" or "012"=>"NDC",
            "OC" or "ORDEN DE COMPRA"=>"OC","RMC" or "REMITO" or "REMITO DE COMPRA"=>"RMC",
            _=>throw new InvalidOperationException("El tipo de comprobante de la lectura no se pudo identificar. Revisalo en Recepción documental; no se convirtió en factura.")
        };
        if(tc is not("FCC" or "NCC" or "NDC"))avisos.Add("Se identificó el tipo original del documento. Su circuito todavía no está implementado; no se lo convirtió en una factura ni se generará un asiento.");
        var moneda=ia.Moneda.Trim().ToUpperInvariant();
        if(moneda is not("" or "1" or "$" or "PES" or "ARS" or "PESOS"))throw new InvalidOperationException("Esta primera etapa admite compras en pesos. No se convirtió la moneda del documento.");
        if(ia.OtrosImpuestos.GetValueOrDefault()!=0)avisos.Add("El documento contiene otros tributos no cubiertos por esta etapa. Su total quedará sin cuadrar hasta ampliar el circuito.");
        var d=new CompraCargaBorrador{Tipo=tc,DocumentoRecibido=documento,LecturaCompraId=ia.Id,LecturaCompraHuella=Huella(ia.JsonResultado),ServiciosGastosConfirmados=false,Proveedor=ia.CuentaContable.Trim(),Letra=ia.Letra.Trim().ToUpperInvariant(),
            Sucursal=ia.PuntoVenta.Trim(),Numero=ia.Numero.Trim(),Fecha=ia.Fecha??DateTime.Today,FechaContable=ia.Fecha??DateTime.Today,
            Vencimiento=ia.Vencimiento??ia.Fecha??DateTime.Today,PercepcionIva=ia.PercepcionIva??0,PercepcionIibb=ia.PercepcionIibb??0,
            PercepcionGanancias=ia.PercepcionGanancias??0,ImpuestoInterno=ia.ImpuestosInternos??0,TotalDocumento=ia.Total};
        var tasas=new[]{(21m,ia.Iva21??0),(10.5m,ia.Iva105??0),(27m,ia.Iva27??0)}.Where(x=>x.Item2!=0).ToList();
        var neto=ia.NetoGravado??0;
        if(neto!=0||tasas.Count>0)
            d.Lineas.Add(new(){Descripcion="Servicios / gastos — neto gravado del documento",Tratamiento="Gravado",Neto=neto,Tasa=tasas.Count==1?tasas[0].Item1:0,IvaInformado=tasas.Sum(x=>x.Item2)});
        if(ia.NetoNoGravado is {} ng&&ng!=0)d.Lineas.Add(new(){Descripcion="Conceptos no gravados del documento",Tratamiento="NoGravado",Neto=ng});
        if(ia.Exento is {} ex&&ex!=0)d.Lineas.Add(new(){Descripcion="Conceptos exentos del documento",Tratamiento="Exento",Neto=ex});
        if(d.Lineas.Count==0)avisos.Add("La lectura no informa bases fiscales utilizables. Completá los conceptos manualmente; no se obtuvo el neto restando impuestos al total.");
        if(tasas.Count>1)avisos.Add("Hay varias alícuotas: separá manualmente las bases e IVA por tasa. No se dedujeron bases dividiendo los importes de IVA.");
        if(ia.Items.Count>0)avisos.Add("Se precarga el resumen fiscal para revisar servicios o gastos. Los renglones originales se conservan en Recepción documental; no se convierten artículos ni se afecta stock.");
        if(ia.Fecha is null)avisos.Add("La lectura no tiene fecha: se muestra hoy provisionalmente. Corregila contra el original.");
        if(!string.IsNullOrWhiteSpace(ia.LectorObservaciones))avisos.Add(ia.LectorObservaciones);
        return d;
    }
}
