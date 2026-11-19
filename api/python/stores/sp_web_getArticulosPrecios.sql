

DECLARE
	@idCliente nvarchar(9) ,
	@searchText nvarchar(100) ,
	@idFamily nvarchar(100) ,
	@idArticulo nvarchar(25)

    SET @idCliente = '#IDCLIENTE'
	SET @searchText = '#SEARCHTEXT'
	SET @idFamily = '#IDFAMILIA'
	SET @idArticulo = '#IDARTICULO'

	DECLARE @idLista NVARCHAR(4)
	DECLARE @clasePrecio NVARCHAR(4)
	DECLARE @claseDefault NVARCHAR(4)
	DECLARE @rs NVARCHAR(MAX)
	DECLARE @wh NVARCHAR(250)
	DECLARE @cotiz NVARCHAR(20)
	DECLARE @cotiz2 NVARCHAR(20)
	DECLARE @cotiz3 NVARCHAR(20)
	DECLARE @cotiz4 NVARCHAR(20)
	DECLARE @cotiz5 NVARCHAR(20)
	DECLARE @coeficiente FLOAT

	DECLARE @ocultaPrecios NVARCHAR(2) = ''

	SET @ocultaPrecios = 'NO'

	SET @cotiz = ISNULL((SELECT ISNULL(MONEDA1,'1')
	FROM TA_COTIZACION
	WHERE FECHA_HORA = CONVERT(date,GETDATE())),'1')
	SET @cotiz2 = ISNULL((SELECT ISNULL(MONEDA2,'1')
	FROM TA_COTIZACION
	WHERE FECHA_HORA = CONVERT(date,GETDATE())),'1')
	SET @cotiz3 = ISNULL((SELECT ISNULL(MONEDA3,'1')
	FROM TA_COTIZACION
	WHERE FECHA_HORA = CONVERT(date,GETDATE())),'1')
	SET @cotiz4 = ISNULL((SELECT ISNULL(MONEDA4,'1')
	FROM TA_COTIZACION
	WHERE FECHA_HORA = CONVERT(date,GETDATE())),'1')
	SET @cotiz5 = ISNULL((SELECT ISNULL(MONEDA5,'1')
	FROM TA_COTIZACION
	WHERE FECHA_HORA = CONVERT(date,GETDATE())),'1')

	SET @claseDefault = (SELECT VALOR
	FROM TA_CONFIGURACION
	WHERE CLAVE='ClaseDePrecioDefault')

	IF @idCliente <>''
		BEGIN
		/*SET @idLista = (SELECT isnull(ltrim(idlista),'')
		from Vt_Clientes
		WHERE CODIGO=@idCliente)
		SET @clasePrecio = (SELECT isnull(clase,@claseDefault)
		from Vt_Clientes
		WHERE CODIGO=@idCliente)*/

		SELECT @idLista = ISNULL(ltrim(idlista),''), @clasePrecio = ISNULL(clase,@claseDefault)
		FROM Vt_Clientes WHERE CODIGO = @idCliente
	END
	ELSE
		BEGIN
		SET @idLista = ''
		SET @clasePrecio = @claseDefault
	END
	
	SET @wh = ''

	If @clasePrecio = '' or @clasePrecio is null or @clasePrecio = 0
		SET @clasePrecio = @claseDefault

	IF @idArticulo <> ''
		SET @wh = ' WHERE ltrim(codigo)= ''' + LTRIM(@idArticulo) + ''''
	ELSE
		BEGIN
		IF @searchText <> ''
				SET @wh = ' WHERE ' + @searchText

		IF @idFamily <> ''
				BEGIN
			IF @wh = ''
						SET @wh = ' WHERE idfamilia like ''' + @idFamily + '%'''
					ELSE 
						SET @wh =  @wh + ' AND (idfamilia like ''' + @idFamily + '%'')'
		END
	END
	

	IF @idLista <> '' 
		SET @rs = '
		SELECT TOP 50 codigo,descripcion,idfamilia,rutaimagen,nombre_familia,cant_of,dto_of,cant_of2,dto_of2,
		codigobarra,tasaiva,convert(varchar,convert(decimal(15,2),costo)) as costo,idunidad,idrubro,idtipo,exento,pesable,
		CASE WHEN ''' + @ocultaPrecios + ''' = ''SI'' THEN ''0'' ELSE convert(varchar,convert(decimal(15,2),precio * coeficiente)) END as precio,
		''' + @ocultaPrecios + ''' as oculta_precio
		 FROM (
		SELECT codigo,descripcion,idfamilia,rutaimagen,nombre_familia,cant_of,dto_of,cant_of2,dto_of2,coeficiente,
		codigobarra,tasaiva,costo,idunidad,idrubro,idtipo,exento,pesable,
		CASE moneda 
		WHEN ''1'' THEN precio
		WHEN ''2'' THEN precio * ' + @cotiz2 + '
		WHEN ''3'' THEN precio * ' + @cotiz3 + '
		WHEN ''4'' THEN precio * ' + @cotiz4 + '
		WHEN ''5'' THEN precio * ' + @cotiz5 + '
		ELSE precio END as precio
		FROM (
		SELECT LTRIM(a.idarticulo) as codigo, b.descripcion,
		b.idfamilia,b.rutaimagen,isnull(c.descripcion,''Sin Familia'') as nombre_familia,
		convert(varchar,convert(decimal(15,2),a.cantidadof2)) as cant_of,
		convert(varchar,convert(decimal(15,2),a.precio9)) as dto_of,
		convert(varchar,convert(decimal(15,2),a.cantidadof3)) as cant_of2,
		convert(varchar,convert(decimal(15,2),a.precio10)) as dto_of2,
		codigobarra,tasaiva,costo,idunidad,idrubro,idtipo,exento,pesable,
		ltrim(b.moneda) as moneda,
		isnull(d.coeficiente,1) as coeficiente,
		CASE WHEN a.ConIva = 1 
		THEN a.precio' + @clasePrecio + ' 
		ELSE ((a.precio' + @clasePrecio + ' * isnull(CASE WHEN b.tasaiva = 0 THEN 21 ELSE b.TasaIVA END,21)) /100) + a.precio' + @clasePrecio + ' END as precio,
		ltrim(b.codigobarra) as codigobarra,b.TasaIVA,b.costo,ltrim(b.idunidad) as idunidad,ltrim(b.idrubro) as idrubro,ltrim(b.idtipo) as idtipo,b.exento,b.pesable
		FROM V_MA_Precios a 
		LEFT JOIN V_MA_ARTICULOS B ON a.IDARTICULO = b.IDARTICULO
		LEFT JOIN V_TA_FAMILIAS c ON b.idfamilia = c.idfamilia
		LEFT JOIN S_TA_EQUIV d ON d.IDUNIDAD = b.UD_TTE and b.IDARTICULO = d.IDARTICULO
		and d.IDUNIDAD_EQUIV = b.IDUNIDAD
		WHERE (b.suspendido=0 or b.suspendido is null) and ltrim(a.idlista)=''' + @idLista + '''
		) as z ' + @wh + ') as x'
	ELSE
		SET @rs = '
		SELECT TOP 50 codigo,descripcion,idfamilia,rutaimagen,nombre_familia,cant_of,dto_of,cant_of2,dto_of2,
		codigobarra,tasaiva,convert(varchar,convert(decimal(15,2),costo)) as costo,idunidad,idrubro,idtipo,exento,pesable,
		CASE WHEN ''' + @ocultaPrecios + ''' = ''SI'' THEN ''0'' ELSE convert(varchar,convert(decimal(15,2),precio * coeficiente)) END as precio,
		''' + @ocultaPrecios + ''' as oculta_precio
		FROM (
		SELECT codigo,descripcion,idfamilia,rutaimagen,nombre_familia,0 as cant_of,0 as dto_of,0 as cant_of2,0 as dto_of2,coeficiente,
		codigobarra,tasaiva,costo,idunidad,idrubro,idtipo,exento,pesable,
		CASE moneda 
		WHEN ''1'' THEN precio
		WHEN ''2'' THEN precio * ' + @cotiz2 + '
		WHEN ''3'' THEN precio * ' + @cotiz3 + '
		WHEN ''4'' THEN precio * ' + @cotiz4 + '
		WHEN ''5'' THEN precio * ' + @cotiz5 + '
		ELSE precio END as precio
		FROM (
		SELECT ltrim(b.idarticulo) as codigo, b.descripcion, b.rutaimagen, isnull(c.descripcion,''Sin Familia'') as nombre_familia,ltrim(c.idfamilia) as idfamilia,
		ltrim(b.codigobarra) as codigobarra,b.TasaIVA,b.costo,ltrim(b.idunidad) as idunidad,ltrim(b.idrubro) as idrubro,ltrim(b.idtipo) as idtipo,b.exento,b.pesable
		, b.precio' + @clasePrecio + ' as precio,isnull(d.coeficiente,1) as coeficiente,
		ltrim(b.moneda) as moneda from v_ma_articulos b
		LEFT JOIN V_TA_FAMILIAS c on b.idfamilia = c.idfamilia
		LEFT JOIN S_TA_EQUIV d ON d.IDUNIDAD = b.UD_TTE and b.IDARTICULO = d.IDARTICULO
		and d.IDUNIDAD_EQUIV = b.IDUNIDAD
		WHERE (b.suspendido=0 or b.suspendido is null) 
		) as z ' + @wh + ' ) as x'
	EXEC(@rs)