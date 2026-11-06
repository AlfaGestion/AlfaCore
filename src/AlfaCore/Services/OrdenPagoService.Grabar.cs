using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class OrdenPagoService
{
    public Task<OrdenPagoResultado> GrabarAsync(OrdenPagoSolicitud solicitud, CancellationToken ct = default)
        => Ejecutar("Grabar", async (cn, user, session, token) =>
        {
            using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
            var resultado = await GrabarAsync(cn, tx, solicitud, user, token);
            VerificarSesion(session); token.ThrowIfCancellationRequested();
            if (!string.Equals(user.Nombre, users.GetCurrentUserName(), StringComparison.OrdinalIgnoreCase))
                throw new OperationCanceledException("El usuario cambió durante la carga de la orden de pago.");
            tx.Commit();
            return resultado;
        }, ct);

    // Para ensayos con transacción externa y rollback, sin modificar la base.
    internal async Task<OrdenPagoResultado> GrabarEnTransaccionAsync(SqlConnection cn, SqlTransaction tx, OrdenPagoSolicitud solicitud, CancellationToken ct)
    {
        var user = await LeerUsuarioAsync(cn, tx, users.GetCurrentUserName(), ct) ?? throw new InvalidOperationException("El usuario actual no está definido en la base activa.");
        return await GrabarAsync(cn, tx, solicitud, user, ct);
    }

    private sealed class ProveedorFila
    {
        public string Codigo { get; set; } = ""; public string Nombre { get; set; } = ""; public string? Calle { get; set; } public string? Numero { get; set; }
        public string? Localidad { get; set; } public string? CPostal { get; set; } public string? Provincia { get; set; } public string? Telefono { get; set; }
        public string? DocumentoTipo { get; set; } public string? DocumentoNumero { get; set; } public string? Iva { get; set; } public string? Motivo { get; set; }
    }
    private sealed class CuentaMedio { public string Codigo { get; set; } = ""; public string Medio { get; set; } = ""; public string Vista { get; set; } = ""; public string Moneda { get; set; } = ""; }
    private sealed class Talonario { public int Numero { get; set; } public string Unidad { get; set; } = ""; public int Sucursal { get; set; } public int Proximo { get; set; } public int Desde { get; set; } public int Hasta { get; set; } }
    private sealed class OperacionGuardada
    {
        public Guid Operacion { get; set; } public int ComprobanteId { get; set; } public string Tc { get; set; } = ""; public string Comprobante { get; set; } = "";
        public int NumeroAsiento { get; set; } public decimal Importe { get; set; } public decimal Anticipo { get; set; } public string SolicitudHash { get; set; } = ""; public string Unidad { get; set; } = "";
    }

    private async Task<OrdenPagoResultado> GrabarAsync(SqlConnection cn, SqlTransaction tx, OrdenPagoSolicitud s, Usuario user, CancellationToken ct)
    {
        CommandDefinition Q(string sql, object? args = null) => Cmd(sql, args, tx, ct);
        var reparto = Repartir(s);
        var unidad = UnidadDe(user);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(s))));
        await cn.ExecuteAsync(Q("""
            DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='ALFACORE_ORDEN_PAGO',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
            IF @r<0 THROW 50001,'Hay otra orden de pago en proceso. Volvé a intentar.',1;
            """));
        var previo = await cn.QuerySingleOrDefaultAsync<OperacionGuardada>(Q("""
            SELECT Operacion,ComprobanteId,Tc,Comprobante,NumeroAsiento,Importe,Anticipo,SolicitudHash,Unidad
            FROM dbo.ALFACORE_PAGO_OPERACION WITH(UPDLOCK,HOLDLOCK) WHERE Operacion=@Operacion;
            """, new { s.Operacion }));
        if (previo is not null)
        {
            if (!user.Admin && previo.Unidad.Trim() != unidad) throw new InvalidOperationException("Esta operación pertenece a otra unidad de negocio.");
            if (previo.SolicitudHash != hash) throw new InvalidOperationException("Esta operación ya fue confirmada con otros datos. Volvé a abrir la orden de pago.");
            return new(previo.Operacion, previo.ComprobanteId, previo.Tc.Trim(), previo.Comprobante, previo.NumeroAsiento, previo.Importe, previo.Anticipo);
        }

        // Proforma: solo si la base la usa y el usuario puede verla.
        var usaProforma = (await ConfigAsync(cn, tx, "USAFACTURAPROFORMA", ct)).Equals("SI", StringComparison.OrdinalIgnoreCase);
        if (s.Proforma && !(usaProforma && user.VerProforma)) throw new InvalidOperationException("No tenés habilitado el pago de proformas.");
        var mezcla = (await ConfigAsync(cn, tx, "MEZCLARPROFORMAS", ct)).Equals("SI", StringComparison.OrdinalIgnoreCase);
        var filtraUnidad = (await ConfigAsync(cn, tx, "CHEQUESXUNEGOCIO", ct)).Equals("SI", StringComparison.OrdinalIgnoreCase);
        var tc = s.Proforma ? TcProforma : TcOrdenPago;

        var proveedor = await cn.QuerySingleOrDefaultAsync<ProveedorFila>(Q("""
            SELECT TOP(1) LTRIM(RTRIM(CODIGO)) Codigo,LEFT(LTRIM(RTRIM(RAZON_SOCIAL)),50) Nombre,CALLE Calle,NUMERO Numero,LOCALIDAD Localidad,CPOSTAL CPostal,
                PROVINCIA Provincia,TELEFONO Telefono,DOCUMENTO_TIPO DocumentoTipo,NUMERO_DOCUMENTO DocumentoNumero,IVA Iva,IdMotivoCpra Motivo
            FROM dbo.Vt_Proveedores WHERE LTRIM(RTRIM(CODIGO))=@proveedor AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0;
            """, new { proveedor = s.Proveedor.Trim() })) ?? throw new InvalidOperationException("El proveedor no existe o está dado de baja.");

        // Comprobantes y créditos: se bloquean sus asientos y aplicaciones y se revalida el saldo actual.
        async Task<OrdenPagoDocumento> Revalidar(OrdenPagoImporte pedido, bool credito)
        {
            var c = pedido.Clave;
            await cn.ExecuteScalarAsync<int>(Q("""
                SELECT COUNT(*) FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK)
                WHERE LTRIM(RTRIM(CUENTA))=@cuenta AND TC=@tc AND SUCURSAL=@suc AND NUMERO=@num AND LETRA=@letra;
                SELECT COUNT(*) FROM dbo.MV_APLICACION WITH(UPDLOCK,HOLDLOCK)
                WHERE LTRIM(RTRIM(CUENTA))=@cuenta AND ((TCO_ORIGEN=@tc AND SUCURSAL_ORIGEN=@suc AND NUMERO_ORIGEN=@num AND LETRA_ORIGEN=@letra)
                    OR (TC=@tc AND SUCURSAL=@suc AND NUMERO=@num AND LETRA=@letra));
                """, new { cuenta = proveedor.Codigo, tc = c.Tc, suc = c.Sucursal, num = c.Numero, letra = c.Letra }));
            var filas = await DocumentosAsync(cn, credito ? "CO_CPTES_CREDITOS_PENDIENTES" : "CO_CPTES_IMPAGOS", proveedor.Codigo, filtraUnidad ? unidad : "", credito, tx, ct);
            var doc = filas.SingleOrDefault(d => d.Clave == c)
                ?? throw new InvalidOperationException($"El comprobante {c.Tc} {c.Sucursal}-{c.Numero} ya no tiene saldo pendiente o no está disponible. Actualizá la selección.");
            if (!doc.Seleccionable) throw new InvalidOperationException($"{c.Tc} {c.Sucursal}-{c.Numero}: {doc.Motivo}");
            if (!CorrespondeAlModo(doc, credito, s.Proforma, mezcla))
                throw new InvalidOperationException($"{c.Tc} {c.Sucursal}-{c.Numero} no corresponde a este tipo de orden ({(s.Proforma ? "proforma" : "no proforma")}).");
            if (pedido.Importe > doc.Saldo)
                throw new InvalidOperationException($"El importe de {c.Tc} {c.Sucursal}-{c.Numero} ({pedido.Importe:N2}) supera su saldo actual ({doc.Saldo:N2}). Actualizá los importes.");
            return doc;
        }
        foreach (var comprobante in s.Comprobantes) await Revalidar(comprobante, false);
        foreach (var credito in s.Creditos) await Revalidar(credito, true);

        // Medios de pago: solo efectivo, banco y cheque propio en pesos.
        var lineas = new List<(OrdenPagoMedioLinea Linea, CuentaMedio Cuenta, string? IdBanco, string? NroCheque)>();
        var cheques = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in s.Medios)
        {
            var cuenta = await cn.QuerySingleOrDefaultAsync<CuentaMedio>(Q("""
                SELECT LTRIM(RTRIM(CODIGO)) Codigo,LTRIM(RTRIM(MedioDePago)) Medio,LTRIM(RTRIM(ISNULL(TipoVista,''))) Vista,LTRIM(RTRIM(MONEDA)) Moneda
                FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO))=@cuenta AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0;
                """, new { cuenta = m.Cuenta.Trim() }));
            if (cuenta is null || cuenta.Moneda != "1" || !(cuenta.Medio is "EF" or "DB" || (cuenta.Medio == "CH" && cuenta.Vista == "BC")))
                throw new InvalidOperationException($"La cuenta {m.Cuenta.Trim()} no es un medio de pago habilitado (efectivo, banco o cheque propio, en pesos).");
            if (cuenta.Medio != "CH") { lineas.Add((m, cuenta, null, null)); continue; }

            var serie = (m.Serie ?? "").Trim().ToUpperInvariant(); var numeroTxt = (m.NumeroCheque ?? "").Trim();
            if (serie.Length != 1 || !long.TryParse(numeroTxt, out var numero) || numero <= 0)
                throw new InvalidOperationException($"Cheque de {cuenta.Codigo}: informá la serie (una letra) y el número del cheque.");
            if (m.FechaCheque is not { } fechaCheque || fechaCheque.Date < s.Fecha.Date)
                throw new InvalidOperationException($"Cheque {serie}-{numero}: la fecha de pago no puede ser anterior a la de la orden.");
            if (m.Clearing is not (0 or 24 or 48 or 72 or 96 or 120 or 144 or 168))
                throw new InvalidOperationException($"Cheque {serie}-{numero}: el clearing es 0, 24, 48, 72, 96, 120, 144 o 168 horas.");
            var banco = await cn.QueryFirstOrDefaultAsync<string>(Q("""
                SELECT TOP(1) IDBANCO FROM dbo.V_TA_CTRL_CHEQUERAS WITH(UPDLOCK,HOLDLOCK)
                WHERE LTRIM(RTRIM(SERIE))=@serie AND LTRIM(RTRIM(CuentaBancoDiferido))=@cuenta AND @numero BETWEEN NUMERO_DESDE AND NUMERO_HASTA
                    AND (@banco='' OR LTRIM(RTRIM(IDBANCO))=@banco);
                """, new { serie, cuenta = cuenta.Codigo, numero = (double)numero, banco = (m.IdBanco ?? "").Trim() }))
                ?? throw new InvalidOperationException($"El cheque {serie}-{numero} no está en ninguna chequera de {cuenta.Codigo}. Revisá la serie, el número y el banco.");
            var nro = $"{serie}-{numeroTxt}";
            if (!cheques.Add($"{cuenta.Codigo}|{nro}")) throw new InvalidOperationException($"El cheque {nro} está repetido en la orden.");
            var emitido = await cn.ExecuteScalarAsync<int>(Q("""
                SELECT COUNT(*) FROM dbo.V_ChequesFirmados WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(CUENTA))=@cuenta AND LTRIM(RTRIM(NroComprobanteBancario))=@nro;
                """, new { cuenta = cuenta.Codigo, nro }));
            if (emitido > 0) throw new InvalidOperationException($"El cheque propio {nro} ya fue emitido.");
            lineas.Add((m, cuenta, banco, nro));
        }

        // Retención de Ganancias: se recalcula acá con los datos actuales; si difiere de la que vio el usuario, se rechaza.
        var (retencion, ctxRet) = await CalcularRetencionAsync(cn, tx, s, null, ct);
        if (retencion.Importe != s.Retencion)
            throw new InvalidOperationException($"La retención de Ganancias cambió: ahora es {retencion.Importe:N2} y la pantalla tenía {s.Retencion:N2}. Volvé a calcularla y revisá los medios de pago.");
        var certificado = 0L;
        if (retencion.Importe > 0)
            certificado = 1 + await cn.ExecuteScalarAsync<long>(Q("""
                SELECT ISNULL(MAX(CONVERT(bigint,RETGAN_NRO)),0) FROM dbo.V_MV_CPTE WITH(UPDLOCK,HOLDLOCK)
                WHERE LTRIM(RTRIM(TC)) IN ('PG','OPG') AND RETGAN_Importe>0 AND TCORIGEN<>'0';
                """));

        // Numeración comercial: talonario del tipo (NUM_<TC> si hay equivalencia) de la unidad, o el general.
        var numerarEn = await ConfigAsync(cn, tx, "NUM_" + tc, ct); if (numerarEn.Length == 0) numerarEn = tc;
        await cn.ExecuteScalarAsync<int>(Q("SELECT COUNT(*) FROM dbo.V_TA_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(CODIGO))=@numerarEn;", new { numerarEn }));
        var talonarios = (await cn.QueryAsync<Talonario>(Q("""
            SELECT Talonario Numero,LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) Unidad,X_SUC_DEFAULT Sucursal,X_ULTIMO_NRO Proximo,X_NRO_DESDE Desde,X_NRO_HASTA Hasta
            FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))=@numerarEn AND CHARINDEX('X',LETRAS)>0 AND ISNULL(Externo,0)=0
                AND LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) IN ('','0',@unidad) ORDER BY Talonario;
            """, new { numerarEn, unidad }))).AsList();
        var tal = talonarios.FirstOrDefault(t => t.Unidad == unidad) ?? talonarios.FirstOrDefault()
            ?? throw new InvalidOperationException($"Falta el talonario de {numerarEn} (letra X) para tu unidad de negocio.");
        if (tal.Sucursal is < 1 or > 9999) throw new InvalidOperationException($"El talonario de {numerarEn} no tiene una sucursal válida.");
        var suc = tal.Sucursal.ToString("D4");
        var max = await cn.ExecuteScalarAsync<long>(Q("""
            SELECT ISNULL(MAX(n),0) FROM (
                SELECT CONVERT(bigint,CASE WHEN LTRIM(RTRIM(NUMERO)) NOT LIKE '%[^0-9]%' AND LTRIM(RTRIM(NUMERO))<>'' THEN LTRIM(RTRIM(NUMERO)) ELSE '0' END) n
                    FROM dbo.V_MV_CPTE WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(TC))=@tc AND SUCURSAL=@suc AND LETRA='X'
                UNION ALL SELECT CONVERT(bigint,CASE WHEN LTRIM(RTRIM(NUMERO)) NOT LIKE '%[^0-9]%' AND LTRIM(RTRIM(NUMERO))<>'' THEN LTRIM(RTRIM(NUMERO)) ELSE '0' END)
                    FROM dbo.MV_ASIENTOS WITH(UPDLOCK,HOLDLOCK) WHERE LTRIM(RTRIM(TC))=@tc AND SUCURSAL=@suc AND LETRA='X'
            ) x;
            """, new { tc, suc }));
        var numeroOrden = Math.Max(Math.Max(tal.Proximo, Math.Max(tal.Desde, 1)), max + 1);
        if (numeroOrden > tal.Hasta || numeroOrden > 99999999) throw new InvalidOperationException($"El talonario de {numerarEn} agotó su rango de numeración.");
        var comprobanteOrden = suc + numeroOrden.ToString("D8") + "X";

        // Contabilidad: ejercicio abierto, equivalencia contable y tipo de registro como el legacy.
        var periodo = await cn.QueryFirstOrDefaultAsync<int?>(Q("""
            SELECT TOP(1) PERIODO FROM dbo.MV_EJERCICIOS WHERE @Fecha BETWEEN [FECHA DESDE] AND [FECHA HASTA] AND CERRADO=0 ORDER BY [FECHA DESDE] DESC;
            """, new { s.Fecha })) ?? throw new InvalidOperationException("La fecha no pertenece a un ejercicio abierto.");
        if (tc == TcOrdenPago)
        {
            var equ = await ConfigAsync(cn, tx, "EQU_OPG", ct);
            if (equ != TcOrdenPago) throw new InvalidOperationException("La orden de pago requiere la equivalencia contable OPG. Revisá EQU_OPG antes de continuar.");
        }
        var tipoReg = await ConfigAsync(cn, tx, "TIPOREGFACTURACION", ct);
        var tipoRegTrim = tipoReg.Trim();
        var asiento = await NumeracionAsiento.SiguienteAsync(cn, tx, periodo.ToString(), s.Fecha.Month, tipoRegTrim, ct);
        var detalle = string.IsNullOrWhiteSpace(s.Detalle) ? $"{(s.Proforma ? "Pago proforma" : "O. de Pago")}: {proveedor.Nombre.Trim()}" : s.Detalle.Trim();
        if (detalle.Length > 100) detalle = detalle[..100];
        var caja = CodigoPk.Format(string.IsNullOrWhiteSpace(user.Caja) ? "1" : user.Caja, 4);
        var unidadPk = CodigoPk.Format(unidad, 4);
        var moneda = CodigoPk.Format("1", 4);
        var dinero = reparto.Dinero;

        var motivo = string.IsNullOrWhiteSpace(proveedor.Motivo) ? null : CodigoPk.Format(proveedor.Motivo.Trim(), 4);
        if (motivo is not null && await cn.ExecuteScalarAsync<int>(Q("""
            IF OBJECT_ID(N'dbo.V_Ta_MotivoVta',N'U') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM dbo.V_Ta_MotivoVta WHERE LTRIM(RTRIM(IdMotivoVta))=@motivo;
            """, new { motivo = motivo.Trim() })) == 0) motivo = null;
        var cabecera = new
        {
            Motivo = motivo,
            Ret = retencion.Importe, RetRegimen = retencion.Importe > 0 ? ctxRet?.RegimenId : null, RetBase = retencion.Importe > 0 ? retencion.BaseActual : (decimal?)null,
            RetNro = retencion.Importe > 0 ? certificado.ToString() : null, RetAlicuota = retencion.Importe > 0 ? ctxRet?.Alicuota : null,
            s.Fecha, Tc = tc, Comprobante = comprobanteOrden, Cuenta = proveedor.Codigo, proveedor.Nombre, Total = dinero, Moneda = moneda, Unidad = unidadPk,
            Usuario = user.Nombre, Detalle = detalle, Suc = suc, Numero = numeroOrden.ToString("D8"),
            Domicilio = $"{proveedor.Calle?.Trim()} {proveedor.Numero?.Trim()}".Trim() is { Length: > 0 } dom ? (dom.Length > 50 ? dom[..50] : dom) : null,
            proveedor.Localidad, CodigoPostal = string.IsNullOrWhiteSpace(proveedor.CPostal) ? null : proveedor.CPostal.Trim(),
            Provincia = string.IsNullOrWhiteSpace(proveedor.Provincia) ? null : CodigoPk.Format(proveedor.Provincia.Trim(), 4),
            proveedor.Telefono, DocTipo = string.IsNullOrWhiteSpace(proveedor.DocumentoTipo) ? null : CodigoPk.Format(proveedor.DocumentoTipo.Trim(), 4),
            DocNumero = proveedor.DocumentoNumero?.Trim(), CondIva = string.IsNullOrWhiteSpace(proveedor.Iva) ? null : CodigoPk.Format(proveedor.Iva.Trim(), 4)
        };
        var id = await cn.ExecuteScalarAsync<int>(Q("""
            INSERT INTO dbo.V_MV_CPTE(TC,IDCOMPROBANTE,IDCOMPLEMENTO,FECHA,FECHAESTINICIO,IDMOTIVOCPRAVTA,CUENTA,NOMBRE,DOMICILIO,LOCALIDAD,CODIGOPOSTAL,IDPROVINCIA,TELEFONO,
                DOCUMENTOTIPO,DOCUMENTONUMERO,CONDICIONIVA,COMENTARIOS,TCORIGEN,COMPROBANTEORIGEN,IMPORTE,MONEDA,FINALIZADA,IdReparto,
                ImpDescuento1,RetGan_Importe,RetIBR_Importe,RetIVA_Importe,RetSUSS_Importe,RETGAN_IdRetencion,RETGAN_BaseImponible,RETGAN_NRO,RETGAN_ALICUOTA,SUCURSAL,NUMERO,LETRA,UNEGOCIO,UNEGOCIO_DESTINO,USUARIO,FechaHora_Grabacion)
            VALUES(@Tc,@Comprobante,0,@Fecha,@Fecha,@Motivo,@Cuenta,@Nombre,@Domicilio,@Localidad,@CodigoPostal,@Provincia,@Telefono,
                @DocTipo,@DocNumero,@CondIva,@Detalle,@Tc,@Comprobante,@Total,@Moneda,1,0,
                0,@Ret,0,0,0,@RetRegimen,@RetBase,@RetNro,@RetAlicuota,@Suc,@Numero,'X',@Unidad,@Unidad,@Usuario,GETDATE());
            SELECT CONVERT(int,SCOPE_IDENTITY());
            """, cabecera));

        var secuencia = 1;
        async Task Linea(string cuenta, decimal importe, string sentido, (OrdenPagoMedioLinea Linea, CuentaMedio Cuenta, string? IdBanco, string? NroCheque)? medio)
        {
            var primera = secuencia == 1; var m = medio;
            var args = new DynamicParameters();
            args.Add("Cuenta", cuenta.Trim()); args.Add("Sec", secuencia++); args.Add("Mes", s.Fecha.Month); args.Add("Asiento", asiento); args.Add("Fecha", s.Fecha);
            args.Add("Detalle", detalle); args.Add("Tc", tc); args.Add("Suc", suc); args.Add("Numero", numeroOrden.ToString("D8")); args.Add("Sentido", sentido);
            args.Add("Importe", importe); args.Add("Moneda", moneda); args.Add("Periodo", periodo.ToString()); args.Add("Total", dinero);
            args.Add("TipoReg", tipoRegTrim.PadRight(4)); args.Add("Usuario", user.Nombre); args.Add("Caja", caja); args.Add("Unidad", unidadPk);
            args.Add("Vencimiento", m?.NroCheque is null ? null : m?.Linea.FechaCheque, DbType.DateTime);
            args.Add("CabCuenta", primera ? proveedor.Codigo : null); args.Add("CabNombre", primera ? proveedor.Nombre : null);
            args.Add("CabCuit", primera ? proveedor.DocumentoNumero?.Trim() : null); args.Add("CabCondIva", primera && !string.IsNullOrWhiteSpace(proveedor.Iva) ? CodigoPk.Format(proveedor.Iva.Trim(), 4) : null);
            args.Add("Retenciones", 0m, DbType.Decimal); args.Add("RetGanancias", primera ? retencion.Importe : 0m, DbType.Decimal);
            var banco = m?.Cuenta.Medio is "DB" or "CH";
            args.Add("IdBanco", m?.IdBanco); args.Add("NroCheque", m?.NroCheque);
            args.Add("Clearing", m?.NroCheque is null ? null : m?.Linea.Clearing, DbType.Int32);
            args.Add("ChPropio", m?.Cuenta.Medio == "CH" ? 1 : 0, DbType.Int32);
            args.Add("Origen", !banco ? null : m?.Cuenta.Medio == "CH" ? m?.Cuenta.Codigo : "-"); args.Add("Destino", banco ? proveedor.Codigo : null);
            await cn.ExecuteAsync(Q("""
                INSERT INTO dbo.MV_ASIENTOS(CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,[DEBE-HABER],IMPORTE,MONEDA,COTIZACION,
                    PERIODO,CABIMPORTE,TIPO_REG,CONTABILIZADO,FechaHora_Grabacion,FechaSubdiario,USUARIO_LOGEADO,IDCAJAS,UNEGOCIO,VENCIMIENTO,
                    CABCUENTA,CABNOMBRE,CABCUIT,CABCONDIVA,LIVA_Ret_Perc,LIVA_Ret_IBtos,LIVA_Ret_Ganancias,
                    IdBanco,NroComprobanteBancario,Clearing,ChPropio,CuentaOrigen,CuentaDestino)
                VALUES(@Cuenta,@Sec,@Mes,@Asiento,@Fecha,@Detalle,@Tc,@Suc,@Numero,'X',@Sentido,@Importe,@Moneda,1,
                    @Periodo,@Total,@TipoReg,0,GETDATE(),@Fecha,@Usuario,@Caja,@Unidad,@Vencimiento,
                    @CabCuenta,@CabNombre,@CabCuit,@CabCondIva,@Retenciones,@Retenciones,@RetGanancias,
                    @IdBanco,@NroCheque,@Clearing,@ChPropio,@Origen,@Destino);
                """, args));
        }
        await Linea(proveedor.Codigo, dinero, "D", null);
        foreach (var m in lineas) await Linea(m.Cuenta.Codigo, m.Linea.Importe, "H", m);
        if (retencion.Importe > 0) await Linea(ctxRet!.Cuenta, retencion.Importe, "H", null);

        async Task Aplicacion(OrdenPagoClave pagador, OrdenPagoClave pagado, decimal importe)
        {
            await cn.ExecuteAsync(Q("""
                INSERT INTO dbo.MV_APLICACION(CUENTA,TC,SUCURSAL,NUMERO,LETRA,IDCOMPROBANTE,TCO_ORIGEN,SUCURSAL_ORIGEN,NUMERO_ORIGEN,LETRA_ORIGEN,
                    IDComprobante_Origen,IDCOMPLEMENTO_ORIGEN,IMPORTE,Transmision,Recepcion,UNegocio,Secuencia,TC_PRINT,IDCOMPROBANTE_PRINT)
                VALUES(@Cuenta,@Tc,@Suc,@Num,@Letra,@Id,@TcO,@SucO,@NumO,@LetraO,@IdO,0,@Importe,0,0,@Unidad,0,@TcPrint,@IdPrint);
                """, new
            {
                Cuenta = proveedor.Codigo, pagador.Tc, Suc = pagador.Sucursal, Num = pagador.Numero, Letra = pagador.Letra, Id = pagador.Comprobante,
                TcO = pagado.Tc, SucO = pagado.Sucursal, NumO = pagado.Numero, LetraO = pagado.Letra, IdO = pagado.Comprobante,
                Importe = importe, Unidad = unidadPk, TcPrint = tc, IdPrint = comprobanteOrden
            }));
        }
        var orden = new OrdenPagoClave(tc, suc, numeroOrden.ToString("D8"), "X");
        foreach (var (credito, factura, importe) in reparto.PorCredito) await Aplicacion(credito, factura, importe);
        foreach (var propia in reparto.Propias) await Aplicacion(orden, propia.Clave, propia.Importe);

        if (await cn.ExecuteScalarAsync<decimal>(Q("""
            SELECT ISNULL(SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END),0) FROM dbo.MV_ASIENTOS
            WHERE LTRIM(RTRIM(PERIODO))=@Periodo AND MES_OPERATIVO=@Mes AND LTRIM(RTRIM(TIPO_REG))=@TipoReg AND [NUMERO ASIENTO]=@Asiento;
            """, new { Periodo = periodo.ToString(), Mes = s.Fecha.Month, TipoReg = tipoRegTrim, Asiento = asiento })) != 0)
            throw new InvalidOperationException("El asiento de la orden de pago no balancea. No se grabaron cambios.");

        await cn.ExecuteAsync(Q("""
            UPDATE dbo.V_TA_CPTE SET X_ULTIMO_NRO=CASE WHEN X_ULTIMO_NRO<@proximo THEN @proximo ELSE X_ULTIMO_NRO END
                WHERE LTRIM(RTRIM(CODIGO))=@numerarEn AND Talonario=@Talonario AND LTRIM(RTRIM(ISNULL(UNEGOCIO,'')))=@UnidadTal;
            INSERT INTO dbo.ALFACORE_PAGO_OPERACION(Operacion,ComprobanteId,Tc,Comprobante,NumeroAsiento,Importe,Anticipo,SolicitudHash,Unidad,Usuario)
            VALUES(@Operacion,@id,@tc,@comprobante,@asiento,@total,@anticipo,@hash,@unidad,@usuario);
            """, new
        {
            proximo = (int)numeroOrden + 1, numerarEn, Talonario = tal.Numero, UnidadTal = tal.Unidad, s.Operacion, id, tc, comprobante = comprobanteOrden, asiento,
            total = dinero, anticipo = reparto.Anticipo, hash, unidad = unidadPk, usuario = user.Nombre
        }));
        await ComprobanteAcciones.RegistrarAsync(cn, tx, tc, comprobanteOrden, ComprobanteAcciones.Alta, user.Nombre, "AlfaCore · Orden de pago", ct,
            comentario: "Alta desde AlfaCore", asiento: asiento, periodo: periodo.ToString(), tipoReg: tipoRegTrim.PadRight(4), fecha: s.Fecha, cuenta: proveedor.Codigo);
        return new(s.Operacion, id, tc, comprobanteOrden, asiento, dinero, reparto.Anticipo);
    }
}
