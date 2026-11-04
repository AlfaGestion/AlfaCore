SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.INT_ESTADO',N'U') IS NULL RETURN;
IF NOT EXISTS(SELECT 1 FROM dbo.INT_ESTADO WHERE Codigo=N'REGISTRADO')
    INSERT dbo.INT_ESTADO(Codigo,Descripcion,Orden,Activo,PermiteEdicion,EsInicial,EsFinal,Color)
    VALUES(N'REGISTRADO',N'Registrado',90,1,0,0,1,N'verde');
UPDATE dbo.INT_ESTADO SET Activo=1,PermiteEdicion=0,EsInicial=0,EsFinal=1 WHERE Codigo=N'REGISTRADO';
-- Completar recepciones de altas web anteriores, sin cambiar facturas, asientos ni lecturas históricas.
IF OBJECT_ID(N'dbo.ALFACORE_COMPRA_OPERACION',N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.INT_COMPROBANTE_RECIBIDO_HIST',N'U') IS NOT NULL
EXEC sys.sp_executesql N'
DECLARE @registrado int; SELECT @registrado=IdEstado FROM dbo.INT_ESTADO WHERE Codigo=N''REGISTRADO'';
INSERT dbo.INT_COMPROBANTE_RECIBIDO_HIST(IdComprobanteRecibido,Usuario,Pc,Accion,IdEstadoAnterior,IdEstadoNuevo,Observacion)
SELECT c.IdComprobanteRecibido,N''SYSTEM'',HOST_NAME(),N''CAMBIO_ESTADO'',c.IdEstado,@registrado,N''Vínculo de compra web anterior recuperado.''
FROM dbo.INT_COMPROBANTE_RECIBIDO c JOIN dbo.ALFACORE_COMPRA_OPERACION o ON o.DocumentoId=c.IdComprobanteRecibido
JOIN dbo.C_MV_CPTE f ON f.ID=o.CompraId WHERE c.Eliminado=0 AND c.IdEstado<>@registrado;
UPDATE c SET IdEstado=@registrado,FechaHoraEstado=GETDATE(),FechaHora_Modificacion=GETDATE(),UsuarioModificacion=N''SYSTEM'',PcModificacion=HOST_NAME()
FROM dbo.INT_COMPROBANTE_RECIBIDO c JOIN dbo.ALFACORE_COMPRA_OPERACION o ON o.DocumentoId=c.IdComprobanteRecibido
JOIN dbo.C_MV_CPTE f ON f.ID=o.CompraId WHERE c.Eliminado=0 AND c.IdEstado<>@registrado;';
