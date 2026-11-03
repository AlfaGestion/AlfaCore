using AlfaCore.Models;

namespace AlfaCore.Services;

// Primera etapa de compras: cálculo y propuesta sin numeración ni escrituras comerciales/fiscales.
internal static class CompraCargaPlanificador
{
    internal static decimal Moneda(decimal n)=>decimal.Round(n,2,MidpointRounding.AwayFromZero);
    internal static string Identidad(CompraCargaBorrador d)
    {
        if(d.Tipo!="FCC")throw new InvalidOperationException("La vista previa contable de ese tipo de comprobante todavía no está implementada. No se lo convirtió en una factura.");
        if(d.Letra is not("A" or "C")||d.Sucursal.Length is <1 or >4||d.Numero.Length is <1 or >8||
            !d.Sucursal.All(char.IsAsciiDigit)||!d.Numero.All(char.IsAsciiDigit)||!int.TryParse(d.Sucursal,out var suc)||suc<1||!int.TryParse(d.Numero,out var n)||n<1)
            throw new InvalidOperationException("Indicá letra A o C, punto de venta de hasta 4 dígitos y número de hasta 8 dígitos, mayores a cero.");
        return $"{suc:0000}{n:00000000}{d.Letra}";
    }
    internal static CompraCargaVista Preparar(CompraCargaBorrador d,CompraCargaProveedor p,CompraCargaContexto c)
    {
        var id=Identidad(d);var errores=new List<string>();var avisos=new List<string>();
        var tipo=c.Tipos.FirstOrDefault(x=>x.Codigo==d.Tipo&&x.Talonario==d.Talonario&&x.Unidad==d.Unidad.Trim())
            ??c.Tipos.FirstOrDefault(x=>x.Codigo==d.Tipo&&x.Talonario==d.Talonario&&x.Unidad.Length==0);
        if(tipo is null||!tipo.Letras.Contains(d.Letra,StringComparison.Ordinal))errores.Add("El tipo, talonario o letra no está configurado para esta unidad de negocio.");
        if(!d.ServiciosGastosConfirmados)errores.Add("Confirmá que el documento corresponde a servicios o gastos sin movimientos de mercadería.");
        if(d.Proveedor.Trim()!=p.Codigo)throw new InvalidOperationException("Volvé a seleccionar el proveedor.");
        if(!c.Unidades.Any(x=>x.Codigo==d.Unidad.Trim()))errores.Add("La unidad de negocio no está autorizada.");
        if(!c.Motivos.Any(x=>x.Codigo==d.Motivo.Trim()))errores.Add("Seleccioná un motivo de compra vigente.");
        if(d.Lineas.Count is 0 or >100)throw new InvalidOperationException("Ingresá entre 1 y 100 conceptos de servicios o gastos.");
        if(d.Fecha.Year<1900||d.FechaContable.Year<1900||d.Vencimiento.Date<d.Fecha.Date)errores.Add("Revisá las fechas y el vencimiento del comprobante.");
        foreach(var fecha in new[]{d.Fecha.Date,d.FechaContable.Date})
            if(c.Desde is {} desde&&fecha<desde.Date||c.Hasta is {} hasta&&fecha>hasta.Date||c.Cierre is {} cierre&&fecha<=cierre.Date)
            {errores.Add("La fecha del comprobante o contable está fuera del rango operativo o dentro del cierre de compras.");break;}
        var regla=c.Reglas.SingleOrDefault(r=>r.Motivo==d.Motivo.Trim()&&r.CondicionIva==p.CondicionIva);
        var asiento=new List<CompraCargaAsiento>();var bases=new List<CompraCargaBaseIva>();
        bool Cuenta(string codigo)=>c.Cuentas.Any(x=>x.Codigo==codigo);
        void Debe(string cuenta,string concepto,decimal monto)
        {
            if(monto==0)return;
            if(!Cuenta(cuenta))errores.Add($"Falta una cuenta contable activa y elegible para {concepto}.");
            var nombre=c.Cuentas.FirstOrDefault(x=>x.Codigo==cuenta)?.Descripcion??concepto;
            asiento.Add(new(cuenta,nombre,Moneda(monto),0));
        }
        for(var i=0;i<d.Lineas.Count;i++)
        {
            var l=d.Lineas[i];
            if(string.IsNullOrWhiteSpace(l.Descripcion)||l.Descripcion.Length>200||l.Neto<=0||l.Neto>100000000000m||l.Tasa<0||l.Tasa>100||l.IvaInformado is <0 or >100000000000m)
            {errores.Add($"Revisá descripción e importes del concepto {i+1}.");continue;}
            if(l.Tratamiento is not("Gravado" or "NoGravado" or "Exento")){errores.Add($"Tratamiento fiscal inválido en el concepto {i+1}.");continue;}
            var neto=Moneda(l.Neto);var calculado=l.Tratamiento=="Gravado"&&d.Letra=="A"?Moneda(neto*l.Tasa/100):0;
            var iva=l.IvaInformado is {} informado?Moneda(informado):calculado;
            if(l.Tratamiento=="Gravado"&&d.Letra=="A"&&(!c.Tasas.Contains(l.Tasa)||l.Tasa==0))errores.Add($"La tasa del concepto {i+1} no está habilitada en la configuración.");
            if((l.Tratamiento!="Gravado"||d.Letra=="C")&&(l.Tasa!=0||iva!=0))errores.Add($"El concepto {i+1} no admite IVA discriminado con ese tratamiento o letra.");
            if(iva!=calculado)avisos.Add($"Concepto {i+1}: se conserva IVA informado {iva:N2}; el cálculo por tasa es {calculado:N2}. Revisá el redondeo contra el original.");
            if(iva!=0&&(p.ExentoServicios||p.ExentoOtros))errores.Add("El proveedor tiene una marca de exención en servicios u otros conceptos; revisá su tratamiento antes de usar IVA.");
            var cuenta=l.Cuenta.Trim();
            if(cuenta.Length==0)cuenta=Cuenta(p.Imputacion)?p.Imputacion:regla?.Compra??"";
            Debe(cuenta,l.Descripcion,neto);
            if(cuenta.Length>0&&cuenta==p.Imputacion)avisos.Add("Se utilizó la imputación particular del proveedor. Verificá que corresponda al servicio o gasto.");
            bases.Add(new(l.Tratamiento,l.Tasa,neto,iva));
        }
        var ivaTotal=bases.Sum(x=>x.Iva);Debe(regla?.Iva??"","IVA crédito fiscal",ivaTotal);
        var percepciones=new[]{(d.PercepcionIva,regla?.PercepcionIva,"Percepción IVA"),(d.PercepcionIibb,regla?.PercepcionIibb,"Percepción Ingresos Brutos"),(d.PercepcionGanancias,regla?.PercepcionGanancias,"Percepción Ganancias")};
        foreach(var (monto,cuenta,concepto) in percepciones)
        {
            if(monto<0||monto>100000000000m){errores.Add($"Importe inválido en {concepto}.");continue;}
            Debe(cuenta??"",concepto,monto);
        }
        var netoTotal=bases.Sum(x=>x.Neto);var total=Moneda(netoTotal+ivaTotal+percepciones.Where(x=>x.Item1>=0&&x.Item1<=100000000000m).Sum(x=>Moneda(x.Item1)));
        asiento.Insert(0,new(p.Codigo,p.Nombre,0,total,true));
        if(total<=0)errores.Add("El total debe ser mayor a cero.");
        if(d.TotalDocumento is <0 or >100000000000m)errores.Add("Revisá el total del documento.");
        var diferencia=d.TotalDocumento is {} totalOriginal?Moneda(total-totalOriginal):(decimal?)null;
        if(diferencia is not null&&diferencia!=0)errores.Add("El total calculado no coincide con el documento. No se agregó un ajuste.");
        if(diferencia is null)avisos.Add("Falta el total del documento para comprobar el cálculo.");
        if(!p.SinOc)avisos.Add("El proveedor no está marcado para facturar sin orden de compra. Esta etapa no valida ni aplica órdenes o remitos.");
        if(d.Letra=="C")avisos.Add("Comprobante C: sin crédito fiscal. La inclusión en el libro respeta MonotributoAlIVA.");
        avisos.Add("Vista previa sin grabación: no registra compras, IVA, stock ni pagos y no reserva numeración.");
        return new(id,netoTotal,ivaTotal,total,diferencia,bases,asiento,errores.Distinct().ToList(),avisos.Distinct().ToList());
    }
}
