-- Reclamo de saldos: mensajes por defecto mejorados (tarjetas + acceso al portal sin usuario ni clave).
-- Solo reemplaza un mensaje si TODAVIA es el texto original sembrado por 2026-11-16-003 (con o sin la linea <TARJETAS>
-- que agrego 2026-11-17-001). Si alguien lo edito, no se toca. Idempotente. Compatible con compatibility level 100.
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
BEGIN
    DECLARE @n nvarchar(4) = NCHAR(13) + NCHAR(10), @nn nvarchar(8) = NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10);
    DECLARE @m TABLE (Clave nvarchar(100), Original nvarchar(max), Nuevo nvarchar(max));

    INSERT INTO @m VALUES
    (N'RECLAMO-SALDOS-MENSAJE1',
     N'Hola <NOMBRE>,' + @nn + N'Te recordamos que al <FECHA> tu cuenta registra comprobantes pendientes de pago. Si ya los abonaste, por favor ignorá este mensaje.' + @nn
        + N'<DETALLE>' + @nn + N'<FORMADEPAGO>' + @nn + N'Muchas gracias. Saludos.',
     N'Hola <NOMBRE>,' + @nn + N'Te recordamos que al <FECHA> tu cuenta registra comprobantes pendientes de pago. Si ya los abonaste, por favor ignorá este mensaje.' + @nn
        + N'<TARJETAS>' + @nn + N'<DETALLE>' + @nn + N'<FORMADEPAGO>' + @nn
        + N'Podés ver el estado completo de tu cuenta, con todos tus comprobantes, desde tu portal de clientes. Entrás con un solo clic, sin usuario ni clave:' + @nn
        + N'<PORTAL>' + @nn + N'Si ya realizaste el pago, respondé este mensaje con el comprobante y lo registramos.' + @nn + N'Muchas gracias. Saludos.'),
    (N'RECLAMO-SALDOS-MENSAJE2',
     N'Hola <NOMBRE>,' + @nn + N'Tu cuenta tiene <VENCIDO> vencidos, con <DIAS> dias de atraso en el comprobante mas antiguo. Te pedimos regularizar el pago a la brevedad.' + @nn
        + N'<DETALLE>' + @nn + N'<FORMADEPAGO>' + @nn + N'Ante cualquier duda, respondé este mensaje. Saludos.',
     N'Hola <NOMBRE>,' + @nn + N'Tu cuenta tiene <VENCIDO> vencidos, con <DIAS> días de atraso en el comprobante más antiguo. Te pedimos regularizar el pago a la brevedad.' + @nn
        + N'<TARJETAS>' + @nn + N'<DETALLE>' + @nn + N'<FORMADEPAGO>' + @nn
        + N'Consultá y regularizá tu cuenta desde tu portal de clientes, sin usuario ni clave:' + @nn
        + N'<PORTAL>' + @nn + N'Si ya realizaste el pago, respondé este mensaje con el comprobante y lo registramos.' + @nn + N'Ante cualquier duda, respondé este mensaje. Saludos.'),
    (N'RECLAMO-SALDOS-MENSAJE3',
     N'Hola <NOMBRE>,' + @nn + N'Pese a nuestros avisos anteriores, tu cuenta mantiene <VENCIDO> vencidos hace <DIAS> dias. Necesitamos que regularices el saldo para poder seguir operando con normalidad.' + @nn
        + N'<DETALLE>' + @nn + N'<FORMADEPAGO>' + @nn + N'Si ya abonaste, enviános el comprobante de pago. Saludos.',
     N'Hola <NOMBRE>,' + @nn + N'Pese a nuestros avisos anteriores, tu cuenta mantiene <VENCIDO> vencidos hace <DIAS> días. Necesitamos que regularices el saldo para poder seguir operando con normalidad.' + @nn
        + N'<TARJETAS>' + @nn + N'<DETALLE>' + @nn + N'<FORMADEPAGO>' + @nn
        + N'Podés ver el detalle y regularizar tu cuenta ahora mismo desde tu portal, sin usuario ni clave:' + @nn
        + N'<PORTAL>' + @nn + N'Si ya abonaste, enviános el comprobante de pago. Saludos.');

    UPDATE t
    SET ValorAux = m.Nuevo, FechaHora_Modificacion = GETDATE()
    FROM dbo.TA_CONFIGURACION t
    JOIN @m m ON m.Clave = UPPER(LTRIM(RTRIM(t.CLAVE)))
    -- Sigue siendo el original (la línea <TARJETAS> del script 001 se descuenta antes de comparar).
    WHERE REPLACE(CONVERT(nvarchar(max), t.ValorAux), N'<TARJETAS>' + @nn, N'') = m.Original;
END
GO
