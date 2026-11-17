-- Reclamo de saldos: agrega la variable <TARJETAS> (cuatro tarjetas con saldo, vencido, comprobantes y dias de atraso)
-- justo antes de <DETALLE> en los mensajes de los tramos 1 a 3, solo si el mensaje todavia no la usa.
-- No toca mensajes sin <DETALLE> ni los que ya tienen <TARJETAS>. Idempotente. Compatible con compatibility level 100.
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
    UPDATE dbo.TA_CONFIGURACION
    SET ValorAux = REPLACE(CONVERT(nvarchar(max), ValorAux), N'<DETALLE>', N'<TARJETAS>' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10) + N'<DETALLE>'),
        FechaHora_Modificacion = GETDATE()
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN (N'RECLAMO-SALDOS-MENSAJE1', N'RECLAMO-SALDOS-MENSAJE2', N'RECLAMO-SALDOS-MENSAJE3')
      AND CONVERT(nvarchar(max), ValorAux) LIKE N'%<DETALLE>%'
      AND CONVERT(nvarchar(max), ValorAux) NOT LIKE N'%<TARJETAS>%';
GO
