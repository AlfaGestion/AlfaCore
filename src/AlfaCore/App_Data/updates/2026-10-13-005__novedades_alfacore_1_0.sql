/*
    Notas de la versión AlfaCore 1.0 en Informes → Novedades.

    Se carga en cada base la primera vez que se entra después del deploy: a todos los usuarios les
    llega un aviso a la campana y la novedad queda guardada en la página Novedades (/novedades).
    No se duplica: si ya existe una novedad con Version = 'AlfaCore 1.0' se actualiza su contenido.
    Crea las tablas de Novedades si la base todavía no las tiene (misma estructura que
    NovedadesService.EnsureSchemaAsync). Las capturas están en /img/novedades/alfacore-1-0/.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_NOVEDADES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_NOVEDADES
    (
        IdNovedad bigint IDENTITY(1,1) NOT NULL,
        Tipo nvarchar(30) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Tipo DEFAULT (N'MANUAL'),
        Estado nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Estado DEFAULT (N'BORRADOR'),
        Prioridad nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Prioridad DEFAULT (N'INFO'),
        Version nvarchar(80) NULL,
        Titulo nvarchar(180) NOT NULL,
        Bajada nvarchar(300) NULL,
        Resumen nvarchar(500) NULL,
        Contenido nvarchar(max) NULL,
        Icono nvarchar(60) NULL,
        Acento nvarchar(20) NULL,
        PortadaTipo nvarchar(20) NOT NULL CONSTRAINT DF_ALFACORE_NOV_PortadaTipo DEFAULT (N'NONE'),
        PortadaValor nvarchar(500) NULL,
        Footer nvarchar(300) NULL,
        FechaProgramada datetime NULL,
        FechaPublicacion datetime NULL,
        VigenteHasta datetime NULL,
        MostrarPopup bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_MostrarPopup DEFAULT (1),
        LecturaObligatoria bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_LecturaObligatoria DEFAULT (0),
        RepetirHastaLeer bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_Repetir DEFAULT (1),
        Segmento nvarchar(30) NOT NULL CONSTRAINT DF_ALFACORE_NOV_Segmento DEFAULT (N'TODOS'),
        UsuarioAlta nvarchar(50) NULL,
        FechaHora_Alta datetime NOT NULL CONSTRAINT DF_ALFACORE_NOV_Alta DEFAULT (GETDATE()),
        UsuarioModificacion nvarchar(50) NULL,
        FechaHora_Modificacion datetime NULL,
        Archivado bit NOT NULL CONSTRAINT DF_ALFACORE_NOV_Archivado DEFAULT (0),
        CONSTRAINT PK_ALFACORE_NOVEDADES PRIMARY KEY CLUSTERED (IdNovedad),
        CONSTRAINT CK_ALFACORE_NOV_Tipo CHECK (Tipo IN (N'MANUAL', N'BOLETIN_PATCH')),
        CONSTRAINT CK_ALFACORE_NOV_Estado CHECK (Estado IN (N'BORRADOR', N'PROGRAMADO', N'PUBLICADO', N'ARCHIVADO')),
        CONSTRAINT CK_ALFACORE_NOV_Prioridad CHECK (Prioridad IN (N'INFO', N'IMPORTANTE', N'CRITICO')),
        CONSTRAINT CK_ALFACORE_NOV_PortadaTipo CHECK (PortadaTipo IN (N'NONE', N'GRADIENT', N'URL'))
    );
END;
GO

IF OBJECT_ID(N'dbo.ALFACORE_NOVEDADES_LECTURAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_NOVEDADES_LECTURAS
    (
        IdNovedad bigint NOT NULL,
        Usuario nvarchar(50) NOT NULL,
        FechaHoraPrimeraVista datetime NOT NULL CONSTRAINT DF_ALFACORE_NOV_LEC_Primera DEFAULT (GETDATE()),
        FechaHoraUltimaVista datetime NOT NULL CONSTRAINT DF_ALFACORE_NOV_LEC_Ultima DEFAULT (GETDATE()),
        FechaHoraLeido datetime NULL,
        VecesVisto int NOT NULL CONSTRAINT DF_ALFACORE_NOV_LEC_Veces DEFAULT (0),
        CONSTRAINT PK_ALFACORE_NOVEDADES_LECTURAS PRIMARY KEY CLUSTERED (IdNovedad, Usuario),
        CONSTRAINT FK_ALFACORE_NOV_LEC_NOVEDAD FOREIGN KEY (IdNovedad)
            REFERENCES dbo.ALFACORE_NOVEDADES (IdNovedad)
    );
END;
GO

DECLARE @Contenido nvarchar(max) = N'<p>Actualizamos AlfaCore con una imagen nueva y un menú más ordenado. Todo lo que ya usabas sigue estando: algunas opciones cambiaron de lugar y varias pantallas suman herramientas nuevas.</p>
<div class="ticket-editor-banner ticket-editor-banner--info"><i class="bi bi-info-circle-fill ticket-editor-banner__icon" contenteditable="false"></i><div class="ticket-editor-banner__body"><p>Cada empresa ve solo los módulos que tiene contratados. Si algo de esta lista no lo ves, es porque tu empresa no usa ese módulo.</p></div></div>
<h2>Nueva imagen</h2>
<div class="nov-features"><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-grid-3x3-gap"></i></span><div class="nov-feature__body"><p><strong>Módulos en el Inicio</strong></p><p>Cada módulo es un mosaico. Al tocarlo se abren sus opciones.</p></div></div><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-search"></i></span><div class="nov-feature__body"><p><strong>Buscador con Ctrl + K</strong></p><p>Escribí el nombre de cualquier opción y entrás directo.</p></div></div><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-star"></i></span><div class="nov-feature__body"><p><strong>Favoritos</strong></p><p>Marcá con la estrella lo que más usás y aparece arriba de todo.</p></div></div><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-funnel"></i></span><div class="nov-feature__body"><p><strong>Filtros a mano</strong></p><p>En tableros y listados los filtros están en la barra de búsqueda.</p></div></div></div>
<div class="ticket-editor-file ticket-editor-file--media" contenteditable="false" tabindex="0" data-ticket-media-block="true"><div class="ticket-editor-file__preview"><img src="/img/novedades/alfacore-1-0/inicio.png" alt="Inicio de AlfaCore con favoritos y módulos"></div></div>
<div class="nov-compare"><div class="nov-compare__col nov-compare__col--antes"><span class="nov-compare__label">Antes</span><p>Un menú lateral largo con todas las opciones mezcladas.</p></div><div class="nov-compare__col nov-compare__col--despues"><span class="nov-compare__label">Ahora</span><p>Un menú arriba en cada pantalla, con sus botones (Nuevo, Guardar, Filtros) a mano.</p></div></div>
<h2>Dónde quedó cada cosa</h2>
<ul><li><strong>Ventas:</strong> Punto de venta, Puntos de venta, Carrito de compras (pedidos de clientes) y Catálogos.</li><li><strong>Compras:</strong> suma el <strong>Reporte de compras</strong>.</li><li><strong>CRM:</strong> Oportunidades, <strong>Cotizaciones</strong> y <strong>Calendario</strong>.</li><li><strong>Tableros:</strong> todos los dashboards juntos (compras, ventas, caja y bancos, stock y contabilidad).</li><li><strong>Utilidades:</strong> la configuración quedó agrupada en la sección Configuración.</li></ul>
<div class="ticket-editor-file ticket-editor-file--media" contenteditable="false" tabindex="0" data-ticket-media-block="true"><div class="ticket-editor-file__preview"><img src="/img/novedades/alfacore-1-0/modulo-abierto.png" alt="Módulo Ventas abierto con sus opciones"></div></div>
<div class="nov-compare"><div class="nov-compare__col nov-compare__col--antes"><span class="nov-compare__label">Antes</span><p>Tocar Ventas te llevaba directo al Punto de venta.</p></div><div class="nov-compare__col nov-compare__col--despues"><span class="nov-compare__label">Ahora</span><p>Tocar Ventas abre sus opciones: Punto de venta, Puntos de venta y Carrito de compras.</p></div></div>
<h2>Calendario</h2>
<div class="nov-features"><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-arrow-repeat"></i></span><div class="nov-feature__body"><p><strong>Eventos que se repiten</strong></p><p>Todos los días, días hábiles, cada semana, cada 15 días, cada mes o a medida.</p></div></div><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-people"></i></span><div class="nov-feature__body"><p><strong>Turnos rotativos</strong></p><p>Cargás a las personas en orden y cada repetición le toca a la siguiente.</p></div></div><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-pin-angle"></i></span><div class="nov-feature__body"><p><strong>Quién está a cargo</strong></p><p>La barra superior muestra quién está a cargo ahora y quién sigue.</p></div></div></div>
<div class="ticket-editor-file ticket-editor-file--media" contenteditable="false" tabindex="0" data-ticket-media-block="true"><div class="ticket-editor-file__preview"><img src="/img/novedades/alfacore-1-0/calendario-repetir.png" alt="Evento que se repite en el Calendario"></div></div>
<div class="nov-compare"><div class="nov-compare__col nov-compare__col--antes"><span class="nov-compare__label">Antes</span><p>Una guardia por semana había que cargarla a mano, semana por semana.</p></div><div class="nov-compare__col nov-compare__col--despues"><span class="nov-compare__label">Ahora</span><p>Cargás una vez "cada semana, rotando entre Juan, Ana y Pedro" y queda armado todo el año.</p></div></div>
<h2>Reuniones y capacitaciones</h2>
<div class="nov-features"><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-calendar-check"></i></span><div class="nov-feature__body"><p><strong>Reserva en tres pasos</strong></p><p>El cliente elige día y horario, completa sus datos y confirma.</p></div></div><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-whatsapp"></i></span><div class="nov-feature__body"><p><strong>Recordatorios automáticos</strong></p><p>Le llega un recordatorio por WhatsApp antes de la reunión, y un aviso si se cancela.</p></div></div><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-calendar-week"></i></span><div class="nov-feature__body"><p><strong>Una por semana</strong></p><p>Cada cliente reserva una reunión por semana desde el link. El equipo agenda las que hagan falta.</p></div></div></div>
<h2>Conversaciones y CRM</h2>
<div class="nov-features"><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-robot"></i></span><div class="nov-feature__body"><p><strong>Asistente por canal</strong></p><p>Elegís en qué canales responde el asistente: web, WhatsApp, Instagram o Facebook.</p></div></div><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-ticket-perforated"></i></span><div class="nov-feature__body"><p><strong>Ticket automático</strong></p><p>Si el asistente no encuentra la respuesta, crea un ticket y le pasa el número al cliente.</p></div></div><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-person-vcard"></i></span><div class="nov-feature__body"><p><strong>Sabés con quién hablás</strong></p><p>En el chat de la web, el asistente pide el WhatsApp y la razón social del visitante.</p></div></div><div class="nov-feature"><span class="nov-feature__icon"><i class="bi bi-file-earmark-pdf"></i></span><div class="nov-feature__body"><p><strong>Plantillas con documento</strong></p><p>Las plantillas de WhatsApp pueden llevar un PDF, por ejemplo el cierre de caja.</p></div></div></div>
<ul><li>Se ven bien los mensajes editados, eliminados y las fotos de "ver una vez".</li><li>Al asignar una oportunidad, también se asigna su conversación y la persona recibe un aviso.</li><li>Las oportunidades ganadas y perdidas siguen visibles en el tablero.</li></ul>
<h2>Avisos y novedades</h2>
<div class="nov-compare"><div class="nov-compare__col nov-compare__col--antes"><span class="nov-compare__label">Antes</span><p>Las novedades aparecían en una ventana que tapaba la pantalla.</p></div><div class="nov-compare__col nov-compare__col--despues"><span class="nov-compare__label">Ahora</span><p>Llegan a la <strong>campana</strong> de la barra superior y quedan guardadas en <strong>Novedades</strong>, para leerlas cuando quieras.</p></div></div>
<h2>Otras mejoras</h2>
<ul><li>La ficha de proveedores ahora es igual a la de clientes.</li><li>Correcciones varias en Carga de viajes, Cotizaciones, Ayuda y Consultas.</li></ul>
<div class="ticket-editor-banner ticket-editor-banner--success"><i class="bi bi-check-circle-fill ticket-editor-banner__icon" contenteditable="false"></i><div class="ticket-editor-banner__body"><p>Si algo no lo encontrás o no funciona como esperabas, escribinos por el chat o abrí un ticket y lo vemos.</p></div></div>';

IF EXISTS (SELECT 1 FROM dbo.ALFACORE_NOVEDADES WHERE Version = N'AlfaCore 1.0')
BEGIN
    UPDATE dbo.ALFACORE_NOVEDADES
    SET Titulo = N'AlfaCore 1.0: nueva imagen y un menú más ordenado',
        Bajada = N'Todo lo nuevo de esta versión, módulo por módulo.',
        Contenido = @Contenido,
        Icono = N'bi-rocket-takeoff',
        Acento = N'cian',
        PortadaTipo = N'NONE',
        MostrarPopup = 0,
        UsuarioModificacion = N'SYSTEM',
        FechaHora_Modificacion = GETDATE()
    WHERE Version = N'AlfaCore 1.0';
END
ELSE
BEGIN
    INSERT INTO dbo.ALFACORE_NOVEDADES
    (
        Tipo, Estado, Prioridad, Version, Titulo, Bajada, Resumen, Contenido, Icono, Acento,
        PortadaTipo, PortadaValor, Footer, FechaPublicacion, MostrarPopup, LecturaObligatoria,
        RepetirHastaLeer, Segmento, UsuarioAlta, FechaHora_Alta, UsuarioModificacion, FechaHora_Modificacion, Archivado
    )
    VALUES
    (
        N'BOLETIN_PATCH', N'PUBLICADO', N'IMPORTANTE', N'AlfaCore 1.0',
        N'AlfaCore 1.0: nueva imagen y un menú más ordenado',
        N'Todo lo nuevo de esta versión, módulo por módulo.',
        N'Nueva imagen de AlfaCore, menú reorganizado, Calendario con eventos que se repiten, reservas de reuniones y mejoras en Conversaciones y CRM.',
        @Contenido,
        N'bi-rocket-takeoff', N'cian', N'NONE', N'', N'Equipo AlfaCore', GETDATE(), 0, 0,
        0, N'TODOS', N'SYSTEM', GETDATE(), N'SYSTEM', GETDATE(), 0
    );
END;
GO
