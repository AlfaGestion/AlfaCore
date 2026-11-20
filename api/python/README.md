# wsAlfa Python

Copia controlada del servicio Python que actualmente se ejecuta en:

`\\10.8.0.6\c\inetpub\wwwroot\wsAlfa`

En el entorno actual, AlfaCore consume `wsAlfa` primero mediante `http://alfanetac.ddns.net/` y usa
`https://alfacentral.ddns.net/` como respaldo si el primer endpoint no está disponible.

## Versiones

- `routes/v1`
- `routes/v2`
- `routes/v3`
- `routes/AGW/V1`
- `routes/AGW/IA/V1`

## Ejecución local

1. Crear un entorno virtual fuera del repositorio o dentro de una carpeta ignorada.
2. Instalar `requirements.txt`.
3. Definir las variables requeridas por `config.py` en el entorno local.
4. Ejecutar `app.py` con Flask o el servidor WSGI configurado.

No copiar `.env`, credenciales, logs ni la carpeta `.venv` al repositorio.
