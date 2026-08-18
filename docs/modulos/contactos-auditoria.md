# Contactos: integración con auditoría de aplicación

## Despliegue del esquema

AlfaCore descubre los scripts de `src/AlfaCore/App_Data/updates/`, los ordena por la fecha y secuencia de su nombre y compara esa versión con `TA_CONFIGURACION.FECHAUPDATE_CORE`. El proceso se ejecuta al iniciar la aplicación y también al cambiar o provisionar una base. Cada resultado queda registrado en `AUX_ACTUALIZACION_HIST`.

Una base legacy cuyo marcador de versión sea anterior recibe los scripts pendientes automáticamente. El update de auditoría fue reemitido como `2026-08-11-002__sistema_auditoria_aplicacion.sql`, una versión posterior al marcador encontrado en las bases de desarrollo, para que también se aplique automáticamente cuando el script original se haya incorporado tardíamente. El SQL es idempotente: completa objetos parciales y no altera destructivamente un esquema ya válido.

`AUX_ACTUALIZACION_HIST` registra por ejecución la versión anterior y nueva, el nombre exacto del archivo, origen, resultado, observación, usuario, equipo y detalle de error. Aunque permite comprobar individualmente qué script se ejecutó, el selector actual usa solamente `FECHAUPDATE_CORE`; por eso un archivo agregado con fecha anterior al marcador queda fuera del conjunto pendiente.

## Compatibilidad transitoria

- Con `SYS_EventosAplicacion` y `SYS_EventosAplicacionCambios` disponibles, Crear, Editar y Dar de baja escriben el evento dentro de la misma conexión y transacción que el cambio en `MA_CONTACTOS`. Un fallo `AUDIT_WRITE_FAILED` revierte toda la operación.
- Sin el esquema, la operación funcional conserva el comportamiento anterior y se registra el diagnóstico técnico `SCHEMA_NOT_AVAILABLE`. No se genera ni se anuncia actividad persistente.

## Tiempo y usuario

Los eventos persisten `DateTime.Now`, es decir, la hora local del servidor de AlfaCore; la futura UI debe mostrar esa misma convención y no aplicar una conversión UTC adicional. El actor proviene de `IAppUserSessionService`, que sincroniza el usuario interno en el accessor liviano utilizado por el servicio global para evitar dependencias circulares.

## Cambios auditables

Las claves estables y sus etiquetas se definen explícitamente en `ContactoAuditFields`. La comparación se hace sobre el request normalizado que realmente persiste `ContactosService`: strings con `Trim`, y vacío/null tratados como ausencia; booleanos por su valor real. No se usa reflexión ni se auditan propiedades ajenas al DTO funcional.
