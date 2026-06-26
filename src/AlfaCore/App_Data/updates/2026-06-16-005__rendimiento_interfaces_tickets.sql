/*
Rendimiento - Interfaces y Tickets

Objetivo:
- reducir CPU en refrescos de cola IA de Interfaces
- mejorar listados paginados de Tickets
- mantener el script idempotente y seguro para bases sin esos modulos
*/

IF OBJECT_ID(N'dbo.IA_Compras_CAB', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_IA_Compras_CAB_Estado_Proceso'
         AND object_id = OBJECT_ID(N'dbo.IA_Compras_CAB')
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_IA_Compras_CAB_Estado_Proceso
        ON dbo.IA_Compras_CAB (Estado, FechaHora_Proceso DESC, ID DESC)
        INCLUDE (IdComprobanteRecibido, IdAdjuntoFuente, SolicitarCancelacion, Intentos);
END;
GO

IF OBJECT_ID(N'dbo.IA_Compras_CAB', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_IA_Compras_CAB_Pendientes'
         AND object_id = OBJECT_ID(N'dbo.IA_Compras_CAB')
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_IA_Compras_CAB_Pendientes
        ON dbo.IA_Compras_CAB (FechaHora_Proceso ASC, ID ASC)
        INCLUDE (IdComprobanteRecibido, IdAdjuntoFuente, SolicitarCancelacion, Intentos)
        WHERE Estado = N'PENDIENTE_LECTURA';
END;
GO

IF OBJECT_ID(N'dbo.TICK_TICKETS', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_TICK_TICKETS_Listado_Fecha'
         AND object_id = OBJECT_ID(N'dbo.TICK_TICKETS')
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_TICK_TICKETS_Listado_Fecha
        ON dbo.TICK_TICKETS (Baja, FechaHoraAlta DESC, IdTicket DESC)
        INCLUDE (CodigoEstado, IdTecnico, Prioridad, ClienteCodigo, IdContacto, IdConversacion, Numero, FechaHoraModificacion, FechaHoraCierre);
END;
GO

IF OBJECT_ID(N'dbo.TICK_TICKETS', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_TICK_TICKETS_Cliente_Fecha'
         AND object_id = OBJECT_ID(N'dbo.TICK_TICKETS')
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_TICK_TICKETS_Cliente_Fecha
        ON dbo.TICK_TICKETS (ClienteCodigo, Baja, FechaHoraAlta DESC, IdTicket DESC)
        INCLUDE (CodigoEstado, IdTecnico, Prioridad, IdContacto, Numero, FechaHoraModificacion);
END;
GO
