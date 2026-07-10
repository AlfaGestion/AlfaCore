AlfaCore - Instalacion
======================

1. Generar el instalador

Ejecutar desde la raiz del repositorio:

    instalador\generar_instalador.bat

Esto:
- publica AlfaCore en modo Release
- prepara la carpeta instalador\AlfaCore\
- compila AlfaCoreSetup.exe con Inno Setup si esta instalado

Si Inno Setup no esta instalado, la carpeta queda preparada igual y el script informa como compilar manualmente.

2. Instalar en el cliente

Ejecutar como administrador:

    AlfaCoreSetup.exe

Durante la instalacion:
- elegir la carpeta de instalacion
- cargar la conexion SQL Server
- elegir el puerto web
- instalar el servicio Windows AlfaCore
- abrir el puerto en Firewall si se dejo marcada la opcion

3. Servicio de Windows

Nombre del servicio: AlfaCore

Comandos utiles:

    sc query AlfaCore
    sc stop AlfaCore
    sc start AlfaCore

4. URLs de acceso

Local:

    http://localhost:5055

Red:

    http://NOMBRE-PC:5055

Si se eligio otro puerto durante la instalacion, usar ese puerto en la URL.

5. Configuracion SQL

La instalacion crea o deja listo:

    appsettings.json

Ese archivo guarda:
- servidor SQL
- base de datos
- usuario
- contraseña
- puerto web

Para cambiarlo despues:
- editar appsettings.json
- o reinstalar sobre la misma carpeta sin borrar ese archivo

6. Firewall

La regla sugerida es:

    AlfaCoreLAN-5055

Si se cambió el puerto web, ejecutar nuevamente el script de firewall.

7. Desinstalar

Usar el desinstalador de Windows o ejecutar:

    sc stop AlfaCore
    sc delete AlfaCore

8. Archivos importantes

- appsettings.json: configuración real del cliente
- appsettings.Server.sample.json: plantilla opcional del repositorio
- scripts\instalar_servicio.bat: instala el servicio
- scripts\desinstalar_servicio.bat: elimina el servicio
- scripts\abrir_firewall.bat: abre el puerto configurado
- scripts\detener_servicio.bat: detiene el servicio

9. Observaciones

- No copiar en producción una appsettings.json de desarrollo.
- No reemplazar manualmente archivos de configuración si ya están personalizados.
- La web queda disponible cuando el servicio termina de iniciar.
