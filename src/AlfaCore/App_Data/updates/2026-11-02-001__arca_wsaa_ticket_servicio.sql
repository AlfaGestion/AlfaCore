/*
    2026-11-02 · Ticket WSAA por servicio (padrón ARCA y otros distintos de wsfe).
    ARCA entrega un solo ticket vigente (~12 h) por certificado y servicio; sin guardarlo, la segunda
    consulta de padrón con el mismo certificado falla con "ya posee un TA válido".
    Lo usa WsaaClient para las bases que consultan con certificado propio (ARCA_CERTIFICADO 'PADRON').
    Idempotente. Compatibility level 100.
*/
IF OBJECT_ID(N'dbo.ARCA_WSAA_TICKET_SERVICIO', N'U') IS NULL
    CREATE TABLE dbo.ARCA_WSAA_TICKET_SERVICIO
    (
        Cuit nvarchar(11) NOT NULL,
        Ambiente nvarchar(20) NOT NULL,
        Servicio nvarchar(60) NOT NULL,
        Certificado nvarchar(32) NOT NULL,
        Token nvarchar(max) NOT NULL,
        Sign nvarchar(max) NOT NULL,
        FechaGeneracionUtc datetime NOT NULL,
        FechaExpiracionUtc datetime NOT NULL,
        CONSTRAINT PK_ARCA_WSAA_TICKET_SERVICIO PRIMARY KEY CLUSTERED (Cuit, Ambiente, Servicio)
    );