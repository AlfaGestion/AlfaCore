# AlfaCore - Alfa Gestión

## Instalación desde el Setup.exe (recomendado)

1. Ejecutá `AlfaCoreSetup_X.X.X.exe` como administrador.
2. El instalador verifica si falta el .NET 8 Hosting Bundle y lo instala automáticamente.
3. Durante la instalación podés elegir:
   - ✅ **Instalar como servicio de Windows** — la app arranca sola con el servidor (recomendado).
   - Abrir el puerto en Firewall de Windows.
   - Abrir el manual al finalizar.
4. La primera vez que la app arranca pedirá los datos de conexión SQL Server por consola.
5. Esos datos se guardan en `appsettings.Production.json` y no se vuelven a pedir.
6. El instalador no incluye configuraciones reales de base de datos ni el contenido de `App_Data` del entorno de desarrollo.

---

## Primera configuración de conexión

Si `appsettings.Production.json` no existe o no tiene datos de conexión, la app los pedirá por consola al iniciar:

```
Servidor: AGSERVER\ALFANET
Base:      ALFANET
Usuario:   ALFANET
Clave:     ****
```

Los datos quedan guardados en `appsettings.Production.json` en la carpeta de instalación.  
Si necesitás cambiarlos, editá ese archivo directamente o eliminalo para que los vuelva a pedir.

---

## Servicio de Windows

El setup instala la app como servicio de Windows (`AlfaCore`). Esto significa que:
- Arranca automáticamente con el servidor.
- Se reinicia solo si falla (hasta 3 reintentos).
- No requiere que nadie inicie sesión para que funcione.

**Gestionar el servicio:**
```batch
# Ver estado
sc query AlfaCore

# Iniciar / detener
sc start AlfaCore
sc stop AlfaCore

# O desde el buscador de Windows: services.msc
```

**Scripts manuales** (en la carpeta de instalación o en `scripts\`):
- `instalar_servicio.bat` — instala o reinstala el servicio (requiere admin).
- `desinstalar_servicio.bat` — detiene y elimina el servicio (requiere admin).

Al desinstalar la app con el desinstalador de Windows, el servicio se elimina automáticamente.

---

## Acceso al dashboard

Desde el servidor:
- `http://localhost:5055`

Desde cualquier PC de la red:
- `http://NOMBRE-PC:5055`
- `http://192.168.1.XX:5055`

Sugerencia: crear un acceso directo en cada cliente apuntando a `http://NOMBRE-PC:5055`.

---

## Instalación manual (sin Setup.exe)

1. Ejecutá `scripts\publicar_release.bat` para generar `publish\AlfaCoreLAN`.
2. Copiá esa carpeta a la PC servidor.
3. Editá `appsettings.Production.json` con los datos de conexión y puerto.
4. Ejecutá `abrir_firewall.bat` como administrador.
5. Para instalar el servicio: ejecutá `instalar_servicio.bat` como administrador.
6. Para iniciar sin servicio: ejecutá `iniciar_AlfaCore.bat`.

---

## Generar el instalador Setup.exe

```batch
scripts\publicar_instalador.bat 1.0.0
```

El script hace todo en orden:
1. Publica la app en `publish\AlfaCoreLAN`.
2. Copia los archivos a `publish\AlfaCoreInstaller\Input` (connection string vacío).
3. Descarga o usa el .NET 8 Hosting Bundle cacheado en `installer\prereqs\`.
4. Compila el instalador con Inno Setup 6 o 7 → `publish\AlfaCoreInstaller\Output\`.

Requisito: tener [Inno Setup](https://jrsoftware.org/isinfo.php) instalado (v6 o v7).  
Si no está instalado, igual queda preparada la carpeta Input para compilar manualmente.

---

## Actualizar una instalación existente

Copiá el contenido de `publish\AlfaCoreLAN\` al directorio de instalación, **sin pisar `appsettings.Production.json`**:

```batch
robocopy publish\AlfaCoreLAN "C:\ruta\instalacion" /MIR /XF appsettings.Production.json *.log
```

Después reiniciá el servicio:
```batch
sc stop AlfaCore && sc start AlfaCore
```

---

## Configuración

### appsettings.Production.json
Archivo con la configuración real del servidor. Se crea automáticamente la primera vez o se puede editar a mano:

```json
{
  "ConnectionStrings": {
    "AlfaGestion": "Server=AGSERVER\\ALFANET;Database=ALFANET;User ID=ALFANET;Password=ALFANET;TrustServerCertificate=True;"
  },
  "ServidorWeb": {
    "Puerto": 5055,
    "EscucharEnRed": true
  }
}
```

### Separación de archivos
| Archivo | Uso |
|---|---|
| `appsettings.json` | Configuración base (no tocar en producción) |
| `appsettings.Production.json` | Configuración real del servidor |
| `appsettings.Server.sample.json` | Plantilla de referencia |

---

## Scripts incluidos

| Script | Descripción |
|---|---|
| `scripts\publicar_release.bat` | Publica la app y arma la carpeta final |
| `scripts\publicar_instalador.bat [version]` | Genera el Setup.exe completo |
| `scripts\instalar_servicio.bat` | Instala el servicio de Windows (requiere admin) |
| `scripts\desinstalar_servicio.bat` | Desinstala el servicio de Windows (requiere admin) |
| `iniciar_AlfaCore.bat` | Inicia la app sin servicio (modo consola) |
| `abrir_firewall.bat` | Abre el puerto en Firewall de Windows |

---

## Solución de problemas

### La app no arranca / servicio no inicia
- Revisá el log: `backend_startup.log` en la carpeta de instalación.
- Verificá que .NET 8 Hosting Bundle esté instalado.
- Revisá datos de conexión en `appsettings.Production.json`.

### Puerto ocupado
- Cambiá `ServidorWeb:Puerto` en `appsettings.Production.json`.
- Ejecutá `abrir_firewall.bat` nuevamente para el nuevo puerto.
- Reiniciá el servicio.

### Firewall bloqueando
- Ejecutá `abrir_firewall.bat` como administrador.
- Verificá que el puerto del firewall coincida con `ServidorWeb:Puerto`.

### Otra PC no puede entrar
- Confirmá que `ServidorWeb:EscucharEnRed` esté en `true`.
- Probá por IP si el nombre de la PC no resuelve.
- Verificá que el firewall esté abierto para ese puerto.

### Falla la conexión SQL Server
- Revisá servidor, base, usuario y clave en `appsettings.Production.json`.
- Confirmá que la PC servidor tenga acceso de red al SQL Server.
- Eliminá `appsettings.Production.json` para volver a ingresar los datos desde cero.

### Cambiar los datos de conexión
- Editá directamente `appsettings.Production.json`, o
- Eliminá el archivo y reiniciá el servicio — la app pedirá los datos nuevamente.
