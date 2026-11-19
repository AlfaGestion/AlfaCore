DECLARE @cuenta nvarchar(9),
	@fechaD nvarchar(10),
	@fechaH nvarchar(10),
	@idvendedor nvarchar(4)

	SET @cuenta = '#CUENTA'
	SET @fechaD = '#FECHAD'
	SET @fechaH = '#FECHAH'
	SET @idvendedor = '#IDVENDEDOR'

	DECLARE @WHERE NVARCHAR(250)
	DECLARE @rs NVARCHAR(MAX)

	SET @WHERE = ''

	IF @cuenta <> ''
		SET @WHERE = ' AND a.Cuenta =''' + @cuenta + ''''

	IF @fechaD != '' AND @fechaH != ''
		SET @WHERE = @WHERE + ' AND (a.Fecha >= ''' + @fechaD + ''' AND a.FECHA<= ''' + @fechaH + ''')'
	ELSE
		BEGIN
		IF @fechaD != ''
				SET @WHERE = @WHERE + ' AND a.Fecha >= ''' + @fechaD + ''''

		IF @fechaH != ''
				SET @WHERE = @WHERE + ' AND a.Fecha <= ''' + @fechaH + ''''
	END

	IF @idvendedor <> ''
		SET @WHERE = @WHERE + ' AND ltrim(a.idvendedor) = ''' + @idvendedor + ''''

	SET @rs = '
	SELECT a.fecha as fechaorden,isnull(ltrim(a.idvendedor),'''') + '' - '' + isnull(b.nombre,'''') as idvendedor,a.cuenta,a.idcomprobante,a.tc,
	CONVERT(NVARCHAR(10),a.fecha,103) as fecha,a.nombre,
	convert(varchar,convert(decimal(15,2),a.importe)) as importe,
	a.finalizada,a.anulada,a.aprobado,
	convert(varchar,convert(decimal(15,2),a.porcdescuento1)) as porcdescuento1,
	convert(varchar,convert(decimal(15,2),a.impdescuento1)) as impdescuento1,
	a.fechafinalizacion,a.fechapreparacion,a.fechahora_grabacion,
	isnull(convert(NVARCHAR(18),a.fechahora_grabacion,103) + '' '' + convert(NVARCHAR(18),a.fechahora_grabacion,108),CONVERT(NVARCHAR(10),a.fecha,103)) as fechagrabacion,
	CASE WHEN a.anulada = 1 THEN ''Anulada''
	ELSE CASE WHEN a.finalizada = 1 THEN ''Finalizada''
	ELSE CASE WHEN a.bloqueada = 1 THEN ''Bloqueada''
	ELSE CASE WHEN not a.fechapreparacion is null THEN ''Preparada''
	ELSE ''Pendiente'' END END END END as estado, isnull(c.lat,'''') as lat,isnull(c.long,'''') as long, 
	convert(NVARCHAR(18),c.fechahora,103) + '' '' + convert(NVARCHAR(18),c.fechahora,108) as fecha_ubicacion
	FROM V_MV_CPTE a 
	LEFT JOIN V_TA_VENDEDORES b ON a.idvendedor = b.idvendedor
	LEFT JOIN S_TA_UBICACIONES_VENDEDOR c on a.IDCOMPROBANTE = c.idcomprobante
	WHERE  a.TC=''NP'' ' + @WHERE + '
	ORDER BY fechaorden DESC'
	--(c.lat<>''0.0'' and c.lat<>''0'')  and
	EXEC(@rs)