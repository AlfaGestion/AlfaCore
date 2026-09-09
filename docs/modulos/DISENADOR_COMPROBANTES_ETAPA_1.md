# Diseñador de comprobantes - Etapa 1

## Alcance

El MVP incorpora plantillas JSON para `COTIZACION`, resolución por `UNegocio`, vista previa HTML y PDF beta con Playwright. El generador histórico `CotizacionPdfService` basado en QuestPDF continúa disponible y no se reemplaza.

Flujo: `COT_VERSION` -> `CotizacionDocumentData` -> `CORE_DocumentTemplate` -> `DocumentRenderer` -> HTML -> preview/PDF beta.

## Base de datos

Aplicar `src/AlfaCore/App_Data/updates/2026-09-08-003__utilidades_disenador_comprobantes.sql` sobre cada base Alfa Gestión. Crea de manera idempotente `CORE_DocumentTemplate`, `CORE_DocumentTemplateVersion`, su índice y la plantilla global de sistema para Cotización.

Las cotizaciones existentes no almacenan `UNegocio`; en esta etapa se elige la unidad en el diseñador, tomando las opciones de `V_TA_UnidadNegocio`. La plantilla específica tiene prioridad sobre la global.

## Playwright

El paquete `Microsoft.Playwright` se restaura junto con el proyecto, pero Chromium se instala por ambiente. Después de publicar, ejecutar una vez desde la carpeta publicada:

```powershell
pwsh .\playwright.ps1 install chromium
```

En servidores sin PowerShell 7, usar el ejecutable `playwright.cmd install chromium` generado por el paquete, o ejecutar el comando equivalente desde el directorio de salida. La cuenta que ejecuta el servicio web debe poder leer el directorio de navegadores de Playwright. No se guardan binarios de Chromium dentro del repositorio.

## Prueba manual

1. Aplicar el script SQL y abrir `Utilidades -> Diseñador de comprobantes`.
2. Elegir una unidad o dejar `Plantilla global` y duplicar `Cotización estándar`.
3. Cambiar márgenes, logo o columnas y guardar; volver a guardar una edición para comprobar el snapshot en `CORE_DocumentTemplateVersion`.
4. Elegir una cotización real y usar `Actualizar vista previa`.
5. Usar `PDF beta` y verificar el archivo generado por Chromium.
6. Abrir la cotización normal y comprobar que `Descargar PDF` sigue usando QuestPDF; `Probar PDF beta` abre el nuevo diseñador sin modificar el documento.
