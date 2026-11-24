# Venta online de AlfaCore (rubros, demo directa, vendedores)

> Estado: fase 1 en desarrollo (2026-11-23). Referencia de modelo comercial: nogosoft.ar.
> Diseño visual aprobado como base: lienzo "AlfaCore — Página de venta" (Claude).

## Objetivo

Vender AlfaCore como abono mensual con un circuito 100 % online:

1. El visitante entra a la página pública y elige **su rubro**.
2. Entra **directo a una demo** con un negocio de ejemplo, sin registrarse.
3. Contrata online, se le crea su base y paga con débito automático.
4. Se da de baja con un botón (botón de baja y de arrepentimiento, Defensa del Consumidor).
5. Vendedores externos traen clientes con su link y cobran comisión. La página de vendedores
   existe pero no se enlaza desde ningún lado (link oculto, `noindex`).

## Qué ya existe y se reutiliza

- Modo SaaS: `ALFA_CENTRAL` (`Clientes`, `bases`, `Modulos`, `ClienteModulos`, `Planes`, `Cargos`).
- Landings de módulos: `/modulos`, `/landing/{slug}` (`LandingContenidoCatalogo`, `wwwroot/css/landing.css`,
  tabla `LandingModulos` para suspender sin tocar código).
- Registro público con prueba de 30 días: `/registrarme` (`ICentralRegistrationService`, reCAPTCHA).
- Login SaaS: `IAppUserSessionService.LoginAsync` + `ILegacyBaseUserSessionService.ResolveByCentralLoginAsync`.
- Jobs: `BackgroundService` registrados en `Program.cs`.

## Fases

### Fase 1 — Página por rubros + demo directa (en curso)

- `/sistema`: página de venta. Grupos de rubros (bares y cafeterías, comercios, distribuidoras,
  servicios técnicos); cada rubro lleva a `/demo/{rubro}`. Contenido en `VentaRubrosCatalogo`
  (`Models/VentaRubrosModels.cs`), igual criterio que `LandingContenidoCatalogo`.
- `/vendedores`: programa de vendedores, `noindex`, sin enlaces desde el sitio.
- `/demo/{rubro}`: resuelve el rubro → familia → demo (`ALFA_CENTRAL.dbo.VentaDemos`), hace login
  automático con el usuario central de la demo y navega a `/{idweb}/{idbase}/{RutaInicial}`.
  - La clave del usuario demo **no** va en la tabla ni en el repo: se lee de configuración
    `VentaDemo:Claves:{FAMILIA}` (user-secrets / `appsettings.Server.json` / variable de entorno
    `VentaDemo__Claves__BARES`).
  - Barra "DEMO" arriba de todo mientras el usuario central sea uno de `VentaDemos`.
- Script central (manual): `docs/base-datos/sql-referencia/venta_demos.sql`.

**Para dejarla andando:**
1. Crear en `ALFA_CENTRAL` un cliente "DEMO" y una base por familia (empezar solo por `BARES`),
   con un negocio de ejemplo cargado (artículos, mesas, clientes, ventas de días anteriores).
2. Crear el usuario central demo de esa base (permisos web restringidos: sin Usuarios,
   Configuración, Actualizaciones ni Administrar).
3. Ejecutar `venta_demos.sql` y cargar la fila de la familia.
4. Poner la clave en configuración (`VentaDemo:Claves:BARES`).
5. ARCA de esa base en homologación (`ARCA_AMBIENTE`), WhatsApp y email sin configurar.

### Fase 2 — Reset nocturno y protecciones

- `VentaDemoResetHostedService`: a las 04:00 restaura cada base demo desde un `.bak` "dorado"
  (`RESTORE ... WITH REPLACE`), cortando conexiones antes. Ruta del `.bak` en `VentaDemos.BackupDorado`.
- Protecciones: no enviar WhatsApp/email desde bases demo, ARCA solo homologación, límite de
  ingresos por IP (`RateLimiter` de ASP.NET Core), sesión demo con vencimiento.
- Selector "Cambiar rubro" en la barra DEMO.

### Fase 3 — Contratación online y cobro

- Reusar `/registrarme`, agregando la familia/rubro elegido (como `ModuloSlug`) para dejar la base
  preconfigurada para ese rubro.
- Cobro con suscripciones de Mercado Pago (preapproval) + webhook que registra pagos en `Cargos`.
- Botón de baja y botón de arrepentimiento en la cuenta del cliente y en el pie de la web.
- Importador de artículos desde Excel en el primer ingreso.

### Fase 4 — Vendedores

- Tabla `VentaVendedores` (código, nombre, WhatsApp, % comisión, activo).
- Link `?v=CODIGO` → cookie 60 días → se guarda el código en el cliente al registrarse.
- Panel del vendedor: sus clientes, estado y comisiones (calculadas desde `Cargos` pagados).

## Pendientes de definir (Alberto)

- Precios de los planes por rubro, y si se venden por familia o por módulos sueltos.
- Comisión de vendedores (% mensual y/o pago por alta).
- Horario de soporte a publicar.
- Dominio público de la página (hoy `/sistema` dentro de AlfaCore).
- Capturas reales para el hero de cada familia.
