# APIs de AlfaCore

Esta carpeta centraliza los servicios HTTP que forman parte de la transición hacia AlfaCore.

## Estructura actual

- `python/`: copia versionada del servicio legado `wsAlfa`.
- `python/routes/v1/`: endpoints legacy de versión 1.
- `python/routes/v2/`: endpoints actuales de versión 2.
- `python/routes/v3/`: endpoints de versión 3.
- `python/routes/AGW/V1/`: endpoints AGW versión 1.
- `python/routes/AGW/IA/V1/`: endpoints de IA versión 1.

La API original continúa operativa en `\\10.8.0.6\c\inetpub\wwwroot\wsAlfa` durante la transición.
Esta copia no reemplaza todavía el despliegue existente.

## Fachada temporal de AlfaCore

AlfaCore puede exponer las mismas rutas y derivarlas al servicio Python mediante:

```text
Api__PythonLegacy__Enabled=true
Api__PythonLegacy__BaseUrl=https://alfacentral.ddns.net/
Api__PythonLegacy__BaseUrls__0=https://alfacentral.ddns.net/
Api__PythonLegacy__TimeoutSeconds=120
```

Mientras `Enabled` sea `false`, esas rutas no se habilitan. La URL debe configurarse en el
servidor o mediante variables de entorno; no se deben guardar credenciales en este repositorio.
En el entorno actual, AlfaCore conserva como único endpoint activo
`https://alfacentral.ddns.net/`, que es el endpoint público usado por los clientes existentes.
`alfanetac.ddns.net` queda reservado para el servidor legacy separado.

## Exclusiones intencionales

No se versionan secretos, entornos virtuales, logs, cachés ni repositorios Git. La configuración
se debe proporcionar mediante variables de entorno o configuración del servidor.

## Próximo paso

Migrar endpoint por endpoint hacia una fachada central de AlfaCore, manteniendo los contratos
`/api/v1`, `/api/v2`, `/api/v3`, `/AGW/V1` e `/ia/v1` hasta completar la transición.
