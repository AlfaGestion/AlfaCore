using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

public sealed class ControlArcaImportacionTests(ITestOutputHelper output)
{
    [Fact]
    public void LaClaveSeOfuscaIgualQueElLegacy()
    {
        // Utl.Codifica("ab"): invertido, Asc + largo con tres dígitos → "100" (b) "099" (a).
        Assert.Equal("100099", ArcaDescargaService.Codifica("ab"));
        Assert.Equal("ab", ArcaDescargaService.Decodifica("100099"));
        foreach (var clave in new[] { "Clave$Fiscal2026", "ñandú.Ñ#1", "x" })
            Assert.Equal(clave, ArcaDescargaService.Decodifica(ArcaDescargaService.Codifica(clave)));
        Assert.Equal("", ArcaDescargaService.Decodifica(""));
    }

    [Fact]
    public void FacturaAConPercepcionArmaElAsientoComoElLegacy()
    {
        var a = new ArcaMisComprobante
        {
            Clase = "FC", Letra = "A", CodigoTipo = 1, Total = 354624.62m, NetoGravado = 308369.23m, Iva = 32378.77m, OtrosTributos = 13876.62m,
            Alicuotas = [new ArcaAlicuota(10.5m, 308369.23m, 32378.77m)]
        };
        var r = ControlArcaImportacionService.Armar(a, "P", "C", "I", "O");
        Assert.Null(r.Error);
        Assert.Equal([new("P", "H", 354624.62m), new("C", "D", 308369.23m), new("I", "D", 32378.77m), new("O", "D", 13876.62m)], r.Lineas);
        Assert.Equal(0, r.Lineas.Sum(l => l.DebeHaber == "D" ? l.Importe : -l.Importe));
        Assert.Equal(10.5m, r.Alicuotas.Single().Tasa);
        Assert.Equal(308369.23m, r.NetoGravado);
    }

    [Fact]
    public void FacturaBOCVaEnteraAComprasYNotaDeCreditoInvierte()
    {
        var b = ControlArcaImportacionService.Armar(new ArcaMisComprobante { Clase = "FC", Letra = "B", Total = 99775m }, "P", "C", "I", "O");
        Assert.Equal([new("P", "H", 99775m), new("C", "D", 99775m)], b.Lineas);
        Assert.Equal(99775m, b.NoGravado);
        Assert.Empty(b.Alicuotas);

        var c = ControlArcaImportacionService.Armar(new ArcaMisComprobante { Clase = "FC", Letra = "C", Total = 1000m, OtrosTributos = 30m }, "P", "C", "I", "O");
        Assert.Equal([new("P", "H", 1000m), new("C", "D", 970m), new("O", "D", 30m)], c.Lineas);

        var nc = ControlArcaImportacionService.Armar(new ArcaMisComprobante
        {
            Clase = "NC", Letra = "A", Total = 121m, NetoGravado = 100m, Iva = 21m, Alicuotas = [new(21m, 100m, 21m)]
        }, "P", "C", "I", "O");
        Assert.Equal([new("P", "D", 121m), new("C", "H", 100m), new("I", "H", 21m)], nc.Lineas);

        Assert.NotNull(ControlArcaImportacionService.Armar(new ArcaMisComprobante { Clase = "FC", Letra = "A", Total = 10m, Iva = 21m }, "P", "C", "I", "O").Error);
    }

    [Fact]
    public void RutasDelScriptComoElLegacy()
    {
        Assert.Equal(@"C:\X\python\python.exe", ArcaDescargaService.Python(@"C:\X\scripts_preproceso\arca_comprobantes.py"));
        Assert.Equal(@"C:\X\ARCA_Descargas", ArcaDescargaService.CarpetaEfectiva("", @"C:\X\scripts_preproceso\arca_comprobantes.py"));
        Assert.Equal(@"D:\Bajadas", ArcaDescargaService.CarpetaEfectiva(@" D:\Bajadas ", @"C:\X\scripts_preproceso\a.py"));
        Assert.Matches(ArcaDescargaService.NombreArchivo, "ARCA_RECIBIDOS_27228073259_202609.xls");
        Assert.DoesNotMatch(ArcaDescargaService.NombreArchivo, @"..\ARCA_RECIBIDOS_27228073259_202609.xls");
    }

    /// <summary>El .xls que deja el script del legacy (Excel 97-2003), con columnas por alícuota.</summary>
    [Fact]
    public void LeeElXlsQueDejaElScript()
    {
        var ruta = Environment.GetEnvironmentVariable("ALFACORE_ARCA_XLS") is { Length: > 0 } r ? r
            : @"C:\NETSISTEMAS\SISTEMAS\NwSistemas\NMC_CONT\ARCA_Descargas\ARCA_RECIBIDOS_27228073259_202609.xls";
        if (!File.Exists(ruta)) return;
        var arca = ControlArcaService.Leer(File.ReadAllBytes(ruta), Path.GetFileName(ruta));
        output.WriteLine($"{(arca.Recibidos ? "Recibidos" : "Emitidos")} · CUIT {arca.Cuit} · {arca.Comprobantes.Count} comprobantes");
        Assert.True(arca.Recibidos);
        Assert.Equal("27228073259", arca.Cuit);
        Assert.NotEmpty(arca.Comprobantes);
        foreach (var c in arca.Comprobantes)
        {
            var armado = ControlArcaImportacionService.Armar(c, "P", "C", "I", "O");
            output.WriteLine($"  {c.Fecha:dd/MM} {c.TipoTexto} {c.PuntoVenta}-{c.NumeroDesde} {c.NombreContraparte} total {c.Total:N2} iva {c.Iva:N2} " +
                             $"[{string.Join(" ", c.Alicuotas.Select(x => $"{x.Tasa}%:{x.Neto:N2}/{x.Iva:N2}"))}] otros {c.OtrosTributos:N2} {armado.Error}");
            if (c.Alicuotas.Count > 0) Assert.Equal(c.Iva, c.Alicuotas.Sum(x => x.Iva));
            Assert.Equal(0, armado.Lineas.Sum(l => l.DebeHaber == "D" ? l.Importe : -l.Importe));
        }
    }
}

/// <summary>Contra ALFANET2007_PRUEBAS: importa un recibido que falta (proveedor existente), controla el asiento y lo que
/// vuelve a decir el control, y lo borra. Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class ControlArcaImportacionPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task ImportaUnRecibidoQueFaltaYQuedaComoCoincide()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", cn.Database);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Alberto'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (method, _) => method.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        var permisos = new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events);
        var control = new ControlArcaService(runtime.Sessions, users, permisos, runtime.Events);
        var cuentas = DispatchProxy.Create<ICuentasComercialesService, CargaComprobantesUiTests.EventOnlyProxy>();
        var padron = DispatchProxy.Create<IArcaPadronService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)cuentas).Handler = (method, _) => method.Name == "GetLookupDataAsync"
            ? Task.FromResult(new CuentaComercialLookupDataDto()) : throw new InvalidOperationException("No se esperaba crear proveedores.");
        var svc = new ControlArcaImportacionService(runtime.Sessions, users, permisos, cuentas, padron, runtime.Events);

        // Un recibido armado con un proveedor real de la base y un número que no existe.
        var prov = await cn.QuerySingleAsync<(string Codigo, string Cuit, string Nombre)>("""
            SELECT TOP (1) LTRIM(RTRIM(CODIGO)), NUMERO_DOCUMENTO, LTRIM(RTRIM(RAZON_SOCIAL)) FROM dbo.VT_PROVEEDORES
            WHERE ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 AND LEN(NUMERO_DOCUMENTO)=11 AND NUMERO_DOCUMENTO NOT LIKE '%[^0-9]%'
              AND LTRIM(RTRIM(ISNULL(IVA,'')))='1' ORDER BY CODIGO;
            """);
        var cuitPropio = await cn.ExecuteScalarAsync<string>("SELECT TOP 1 LTRIM(RTRIM(CUIT)) FROM dbo.V_TA_UnidadNegocio WHERE ISNULL(CUIT,'')<>''");
        const int pv = 9876; var numero = 10000000 + DateTime.Now.Second * 1000 + DateTime.Now.Millisecond;
        var csv = $"Mis Comprobantes Recibidos - CUIT {cuitPropio}\n" +
                  "Fecha;Tipo;Punto de Venta;Número Desde;Número Hasta;Cód. Autorización;Tipo Doc. Emisor;Nro. Doc. Emisor;Denominación Emisor;Tipo Doc. Receptor;Nro. Doc. Receptor;Tipo Cambio;Moneda;" +
                  "IVA 21%;Neto Grav. IVA 21%;IVA 10,5%;Neto Grav. IVA 10,5%;Neto Gravado Total;Neto No Gravado;Op. Exentas;Otros Tributos;Total IVA;Imp. Total\n" +
                  $"20/10/2026;1 - Factura A;{pv};{numero};{numero};86350948638843;CUIT;{prov.Cuit};{prov.Nombre};CUIT;{cuitPropio};1;$;21,00;100,00;10,50;100,00;200,00;5,00;0;3,00;31,50;239,50\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        var r1 = await control.ControlarAsync(bytes, "recibidos.csv");
        var falta = r1.Filas.Single(f => f.PuntoVenta == pv && f.Numero == numero);
        Assert.Equal(ControlArcaEstado.FaltaEnSistema, falta.Estado);
        Assert.Equal(2, falta.Arca!.Alicuotas.Count);

        var ctx = await svc.ContextoAsync([falta.Arca], r1.CuitArchivo);
        output.WriteLine($"Cuentas: compras {ctx.CuentaCompras} · IVA {ctx.CuentaIva} · otros {ctx.CuentaOtrosTributos} · unidad {ctx.Unidad} · proveedor {ctx.Filas[0].Proveedor} «{ctx.Filas[0].Bloqueo}»");
        Assert.Equal(prov.Codigo, ctx.Filas[0].Proveedor);
        Assert.Equal("", ctx.Filas[0].Bloqueo);

        int? asiento = null;
        try
        {
            var res = await svc.ImportarAsync(new ControlArcaImportacionSolicitud
            {
                Comprobantes = [falta.Arca], CuentaCompras = ctx.CuentaCompras, CuentaIva = ctx.CuentaIva, CuentaOtrosTributos = ctx.CuentaOtrosTributos,
                Unidad = ctx.Unidad, ProveedoresNuevos = ControlArcaProveedorModo.Omitir
            });
            output.WriteLine(res[0].Resultado);
            Assert.True(res[0].Importado, res[0].Resultado);
            var lineas = (await cn.QueryAsync<(int Sec, string Cuenta, string Dh, decimal Importe, string? Liva, decimal LivaTotal, decimal Iva1, decimal Al1, decimal Iva2, decimal Al2, decimal Perc, decimal NoGrav, int Asiento)>("""
                SELECT SECUENCIA, LTRIM(RTRIM(CUENTA)), [DEBE-HABER], IMPORTE, LIVA_TIPO, ISNULL(LIVA_TOTAL,0), ISNULL(LIVA_ImpIVA,0), ISNULL(LIVA_AlicIVA,0),
                       ISNULL(LIVA_ImpIVA2,0), ISNULL(LIVA_AlicIVA2,0), ISNULL(LIVA_Ret_Perc,0), ISNULL(LIVA_ImpNetoNGrav,0), [NUMERO ASIENTO]
                FROM dbo.MV_ASIENTOS WHERE TC='FCC' AND SUCURSAL=@s AND NUMERO=@n AND LETRA='A' ORDER BY SECUENCIA;
                """, new { s = pv.ToString("0000"), n = numero.ToString("00000000") })).ToList();
            asiento = lineas[0].Asiento;
            foreach (var l in lineas) output.WriteLine($"  {l.Sec} {l.Cuenta} {l.Dh} {l.Importe:N2} {l.Liva} {l.LivaTotal:N2} iva {l.Al1}%={l.Iva1:N2} {l.Al2}%={l.Iva2:N2} perc {l.Perc:N2} ng {l.NoGrav:N2}");
            Assert.Equal(4, lineas.Count);
            Assert.Equal(0, lineas.Sum(l => l.Dh == "D" ? l.Importe : -l.Importe));
            Assert.Equal((prov.Codigo, "H", 239.50m), (lineas[0].Cuenta, lineas[0].Dh, lineas[0].Importe));
            Assert.Equal(("Compras", 239.50m, 21m, 21m, 10.5m, 10.5m, 3m, 5m), (lineas[0].Liva, lineas[0].LivaTotal, lineas[0].Iva1, lineas[0].Al1, lineas[0].Iva2, lineas[0].Al2, lineas[0].Perc, lineas[0].NoGrav));
            Assert.Equal(205m, lineas.Single(l => l.Cuenta == ctx.CuentaCompras).Importe);

            // Volver a comparar: ahora coincide. Volver a importar: ya está cargado.
            var r2 = await control.ControlarAsync(bytes, "recibidos.csv");
            Assert.Equal(ControlArcaEstado.Coincide, r2.Filas.Single(f => f.PuntoVenta == pv && f.Numero == numero).Estado);
            var otra = await svc.ImportarAsync(new ControlArcaImportacionSolicitud
            {
                Comprobantes = [falta.Arca], CuentaCompras = ctx.CuentaCompras, CuentaIva = ctx.CuentaIva, CuentaOtrosTributos = ctx.CuentaOtrosTributos,
                Unidad = ctx.Unidad, ProveedoresNuevos = ControlArcaProveedorModo.Omitir
            });
            Assert.False(otra[0].Importado);
            Assert.Contains("Ya está cargado", otra[0].Resultado);
        }
        finally
        {
            var id = pv.ToString("0000") + numero.ToString("00000000") + "A";
            await cn.ExecuteAsync("""
                DELETE FROM dbo.MV_ASIENTOS WHERE TC='FCC' AND SUCURSAL=@s AND NUMERO=@n AND LETRA='A' AND USUARIO_LOGEADO='IMPORTACION';
                DELETE FROM dbo.C_MV_CPTE WHERE TC='FCC' AND IDCOMPROBANTE=@id AND USUARIO='IMPORTACION';
                DELETE FROM dbo.V_MV_CPTEACCIONES WHERE TC='FCC' AND IDCOMPROBANTE=@id AND Proceso=N'AlfaCore: importación desde ARCA';
                """, new { s = pv.ToString("0000"), n = numero.ToString("00000000"), id });
            output.WriteLine($"Borrado el asiento {asiento} de prueba.");
        }
    }
}
