# Integración: OpenAI para InformesIA

## Alcance en AlfaCore

InformesIA usa OpenAI desde backend para clasificar consultas en lenguaje natural y, cuando corresponde, generar SQL de solo lectura sobre vistas autorizadas del dashboard de compras. Si OpenAI no está configurado o falla, AlfaCore conserva un modo controlado con consultas predefinidas y validación local.

Código relacionado:

- `src/AlfaCore/Services/InformesIaService.cs`
- `src/AlfaCore/Services/InformesIaService.Helpers.cs`
- `src/AlfaCore/Services/InformesIaService.Queries.cs`
- `src/AlfaCore/Services/InformesIaSqlValidator.cs`
- `src/AlfaCore/Services/InformesIaHistoryStore.cs`
- `src/AlfaCore/Services/InformesIaResultStore.cs`

Documento histórico relacionado: `docs/modulos/InformesIA.md`.

## Configuración

La clave de API no debe guardarse en código ni en frontend.

Variables usadas:

| Variable | Uso |
|---|---|
| `OPENAI_API_KEY` | API key enviada como Bearer desde backend |
| `OPENAI_MODEL` | Modelo a usar; si no está, el servicio aplica un default interno |

## Flujo técnico

1. El usuario escribe una consulta en lenguaje natural.
2. AlfaCore intenta clasificar intención y filtros.
3. Si corresponde, llama a OpenAI por backend.
4. La IA debe devolver una propuesta estructurada o SQL de lectura.
5. Antes de ejecutar, `InformesIaSqlValidator` valida la consulta.
6. Solo se ejecuta si comienza con `SELECT`, no tiene sentencias múltiples ni comandos peligrosos, y usa únicamente vistas autorizadas.
7. Se limita la cantidad de filas y se guardan historial/resultados.

## Vistas autorizadas

- `vw_compras_cabecera_dashboard`
- `vw_compras_detalle_dashboard`
- `vw_estadisticas_ingresos_diarias`
- `vw_familias_jerarquia`

## Controles de seguridad

- La API key vive en variable de entorno.
- Las llamadas a OpenAI se hacen solo desde backend.
- El frontend nunca recibe la API key.
- No se aceptan `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `EXEC`, `MERGE`, `CREATE`, `sp_`, `xp_`, comentarios ni múltiples sentencias.
- Si la IA propone una fuente no autorizada, se rechaza.
- El sistema debe tratar la salida de IA como no confiable hasta validarla.

## Problemas frecuentes

- No configurar `OPENAI_API_KEY` debe degradar con mensaje claro, no romper la pantalla.
- La IA puede inventar columnas o tablas; por eso el validador local es obligatorio.
- Las consultas generadas pueden ser sintácticamente válidas pero funcionalmente pobres; mantener plantillas conocidas mejora estabilidad.
- Los filtros de fechas deben ser explícitos y parametrizados; no conviene dejar que la IA concatene valores del usuario.
- Los resultados deben tener tope de filas para proteger rendimiento.

## Lecciones aplicadas en AlfaCore

- OpenAI es una ayuda para interpretación, no una autoridad para ejecutar SQL.
- La lista blanca de vistas es más segura que intentar bloquear todo lo peligroso por palabras.
- Conviene guardar historial para soporte: pregunta original, éxito/error, SQL generado y fecha.
- Cuando la IA falla, las sugerencias predefinidas mantienen la funcionalidad útil.
- La documentación para clientes debe explicar que InformesIA consulta información existente; no modifica datos.

## Fuentes oficiales

- [OpenAI API: autenticación](https://developers.openai.com/api/reference/overview/)
- [OpenAI API: quickstart](https://developers.openai.com/api/docs/quickstart)
- [OpenAI API: Chat Completions](https://developers.openai.com/api/reference/chat-completions/overview/)
