using AlfaCore.Services;
using ClosedXML.Excel;
using Xunit;

namespace AlfaCore.Tests;

public sealed class ConciliacionDetalleProcesadorTests
{
    private static readonly string[] Cabecera=["Fecha de operación","Fecha de acreditación","Código de operación","Local","Nombre o ID","Medio de Cobro",
        "Nombre de la persona","CUIT/CUIL/DNI","Medio de Pago","Monto bruto","Comisión","IVA comisión","Monto neto","Estado","ID externo de pago"];

    private static byte[] Libro(Action<IXLWorksheet> filas)
    {
        using var wb=new XLWorkbook();
        var ws=wb.AddWorksheet("Sheet0");
        ws.Cell(9,1).Value="Informe de detalles - Todos los medios de cobro";
        for(var i=0;i<Cabecera.Length;i++) ws.Cell(23,i+1).Value=Cabecera[i];
        filas(ws);
        using var ms=new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Fila(IXLWorksheet ws,int fila,string codigo,string medio,double bruto,double com,double iva,double neto)
    {
        ws.Cell(fila,1).Value="01/10/2026 12:34"; ws.Cell(fila,2).Value=" 01/10/2026"; ws.Cell(fila,3).Value=" "+codigo;
        ws.Cell(fila,9).Value=" "+medio; ws.Cell(fila,10).Value=bruto; ws.Cell(fila,11).Value=com; ws.Cell(fila,12).Value=iva; ws.Cell(fila,13).Value=neto;
        ws.Cell(fila,14).Value=" Acreditado";
    }

    [Fact]
    public void Nave_lee_operaciones_y_omite_la_fila_de_totales()
    {
        var lectura=ConciliacionDetalleProcesador.Leer(Libro(ws=>
        {
            Fila(ws,24,"GSY202102702","Mastercard Crédito",10000,480,100.8,9419.2);
            Fila(ws,25,"TWY103994745","Dinero en cuenta",10000,80,16.8,9903.2);
            ws.Cell(26,1).Value="Total"; ws.Cell(26,10).Value=20000;
        }));
        Assert.Equal("NAVE",lectura.Procesador);
        Assert.Equal(2,lectura.Operaciones.Count);
        Assert.Equal("GSY202102702",lectura.Operaciones[0].Codigo);
        Assert.Equal("Mastercard Crédito",lectura.Operaciones[0].MedioPago);
        Assert.Equal(9419.2m,lectura.Operaciones[0].Neto);
        Assert.Equal(new DateTime(2026,10,1),lectura.Desde);
    }

    [Fact]
    public void Operacion_que_no_cierra_se_rechaza()
    {
        var ex=Assert.Throws<InvalidOperationException>(()=>ConciliacionDetalleProcesador.Leer(Libro(ws=>Fila(ws,24,"AAA111","Visa Débito",10000,80,16.8,9999))));
        Assert.Contains("no cierra",ex.Message);
    }

    [Fact]
    public void Pago_y_devolucion_comparten_codigo_y_se_leen_ambos()
    {
        var l=ConciliacionDetalleProcesador.Leer(Libro(ws=>
        {
            Fila(ws,24,"AAA111","Visa Débito",10000,80,16.8,9903.2);
            Fila(ws,25,"AAA111","Visa Débito",-10000,-80,-16.8,-9903.2);
        }));
        Assert.Equal(2,l.Operaciones.Count);
        Assert.Equal(0m,l.Operaciones.Sum(o=>o.Neto));
    }

    [Fact]
    public void Formato_desconocido_no_se_importa()
    {
        using var wb=new XLWorkbook(); wb.AddWorksheet("x").Cell(1,1).Value="otra cosa";
        using var ms=new MemoryStream(); wb.SaveAs(ms);
        Assert.Throws<InvalidOperationException>(()=>ConciliacionDetalleProcesador.Leer(ms.ToArray()));
    }
}

public sealed class ConciliacionLiquidacionTests
{
    private static readonly AlfaCore.Models.ConciliacionAsientoCuenta[] Plan=
    [
        new("11203","I.V.A. C.F. Por Compra de Bienes-Merc. Local"),new("11205","I.V.A. C.F. Por Prestacion de Servicios"),
        new("11208","I.V.A. Retención/Percepción"),new("21201","I.V.A. Debito Fiscal Responsable Inscripto"),
        new("42209","Gastos Bancarios"),new("34","IVA")
    ];

    [Fact]
    public void Propone_el_iva_credito_fiscal_del_activo_y_no_retenciones_ni_debito()
        =>Assert.Equal("11203",ConciliacionBancariaService.SugerirCuentaIvaCredito(Plan));

    [Fact]
    public void Propone_gastos_bancarios_para_la_comision()
        =>Assert.Equal("42209",ConciliacionBancariaService.SugerirCuentaComision(Plan));

    [Fact]
    public void Sin_cuenta_adecuada_no_propone_nada()
    {
        Assert.Null(ConciliacionBancariaService.SugerirCuentaIvaCredito([new("21201","I.V.A. Debito Fiscal"),new("34","IVA")]));
        Assert.Null(ConciliacionBancariaService.SugerirCuentaComision([new("11203","Caja")]));
    }

    private static readonly LiquidacionCuenta[] Cuentas=
    [
        new("111010009","VISA DEBITO",""),new("111010010","VISA CREDITO",""),new("111010012","MASTERCARD CREDITO",""),new("111010008","QR NAVE (en cuenta)","")
    ];

    [Theory]
    [InlineData(1000.0,210.0,21.0)]
    [InlineData(1000.0,105.0,10.5)]
    [InlineData(1000.0,0.0,0.0)]
    [InlineData(1000.0,150.0,null)]
    public void Alicuota_del_libro_solo_acepta_alicuotas_vigentes(double neto,double iva,double? esperada)
        =>Assert.Equal(esperada.HasValue ? (decimal)esperada.Value : null,ConciliacionBancariaService.AlicuotaLibro((decimal)neto,(decimal)iva));

    [Theory]
    [InlineData("Visa Débito","111010009")]
    [InlineData("Visa Crédito Internacional","111010010")]
    [InlineData("Mastercard Crédito","111010012")]
    [InlineData("Dinero en cuenta",null)]
    [InlineData("Visa Prepaga",null)]
    public void Sugiere_la_cuenta_solo_si_el_nombre_coincide(string medio,string? esperado)
        =>Assert.Equal(esperado,ConciliacionBancariaService.SugerirCuentaMedio(medio,Cuentas));
}

public sealed class ConciliacionTarjetasExcelTests
{
    [Fact]
    public void El_excel_de_nave_se_lee_como_liquidacion_con_el_bruto_por_operacion()
    {
        using var wb=new ClosedXML.Excel.XLWorkbook();
        var ws=wb.AddWorksheet("Sheet0");
        ws.Cell(9,1).Value="Informe de detalles - Todos los medios de cobro";
        string[] cab=["Fecha de operación","Fecha de acreditación","Código de operación","Local","Nombre o ID","Medio de Cobro","Nombre de la persona","CUIT/CUIL/DNI","Medio de Pago","Monto bruto","Comisión","IVA comisión","Monto neto","Estado","ID externo de pago"];
        for(var i=0;i<cab.Length;i++) ws.Cell(23,i+1).Value=cab[i];
        ws.Cell(24,1).Value="01/10/2026 12:34"; ws.Cell(24,2).Value=" 03/10/2026"; ws.Cell(24,3).Value=" GSY1"; ws.Cell(24,9).Value=" Visa Débito";
        ws.Cell(24,10).Value=10000; ws.Cell(24,11).Value=80; ws.Cell(24,12).Value=16.8; ws.Cell(24,13).Value=9903.2; ws.Cell(24,14).Value=" Acreditado";
        using var ms=new MemoryStream(); wb.SaveAs(ms);
        var bytes=ms.ToArray();
        Assert.True(ConciliacionTarjetasService.EsExcel(bytes));
        Assert.False(ConciliacionTarjetasService.EsExcel("%PDF-1.4"u8.ToArray()));
        var l=ConciliacionTarjetasService.LecturaDesdeExcel(bytes);
        var d=Assert.Single(l.Detalles);
        Assert.Equal(10000m,d.Importe);
        Assert.Equal(new DateTime(2026,10,1),d.Fecha);
        Assert.Equal(new DateTime(2026,10,3),d.FechaPago);
        Assert.Equal("GSY1",d.Liquidacion);
        Assert.Equal(10000m,l.Bruto);
        Assert.Equal(9903.2m,l.Neto);
    }
}

public sealed class AsientoLibroIvaDeduccionTests
{
    [Fact]
    public void Deduce_iva_neto_y_alicuota_del_asiento_de_liquidacion()
    {
        var l=AsientosService.DeducirLibroIva(
        [
            ("111020001","BANCO GALICIA","D",27467893.85m,"DB"),
            ("42209","Gastos Bancarios","D",440005.91m,""),
            ("11203","I.V.A. C.F. Por Compra de Bienes-Merc. Local","D",92401.25m,""),
            ("111010009","VISA DEBITO","H",28000301.01m,"TJ")
        ]);
        Assert.Equal("COMPRAS",l.Libro);
        Assert.False(l.Resta);
        Assert.Equal(440005.91m,l.NetoGravado);
        Assert.Equal(21m,l.Alicuota);
        Assert.Equal(92401.25m,l.Iva);
    }

    [Fact]
    public void Separa_percepciones_y_retenciones_por_tipo()
    {
        var l=AsientosService.DeducirLibroIva(
        [
            ("1","Gastos","D",1000m,""),("2","I.V.A. Credito Fiscal","D",210m,""),
            ("3","Percepcion IVA","D",30m,""),("4","Percepcion IIBB","D",20m,""),("5","Retencion Ganancias","D",10m,""),
            ("6","Banco","H",1270m,"DB")
        ]);
        Assert.Equal(30m,l.RetencionIva);Assert.Equal(20m,l.RetencionIibb);Assert.Equal(10m,l.RetencionGanancias);
        Assert.Equal(210m,l.Iva);Assert.Equal(1000m,l.NetoGravado);
    }
}
