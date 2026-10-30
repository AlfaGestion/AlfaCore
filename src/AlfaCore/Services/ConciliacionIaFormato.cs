using System.Text.Json;

namespace AlfaCore.Services;

internal static class ConciliacionIaFormato
{
    // JSON mode garantiza sintaxis, no tipos ni campos. El contrato estructurado evita respuestas
    // incompatibles con ConciliacionIaSalida; las reglas contables se validan después en el servidor.
    internal static readonly JsonElement Respuesta=JsonSerializer.Deserialize<JsonElement>("""
        {
          "type":"json_schema",
          "json_schema":{
            "name":"conciliacion_movimientos",
            "strict":true,
            "schema":{
              "type":"object",
              "properties":{
                "grupos":{
                  "type":"array",
                  "items":{
                    "type":"object",
                    "properties":{
                      "banco":{"type":"array","items":{"type":"integer"}},
                      "alfa":{"type":"array","items":{"type":"integer"}},
                      "confianza":{"type":"string","enum":["Alta","Media","Baja"]},
                      "motivo":{"type":"string"}
                    },
                    "required":["banco","alfa","confianza","motivo"],
                    "additionalProperties":false
                  }
                },
                "orientaciones":{
                  "type":"array",
                  "items":{
                    "type":"object",
                    "properties":{
                      "banco":{"type":"integer"},
                      "categoria":{"type":"string"},
                      "cuenta":{"type":"string"},
                      "motivo":{"type":"string"}
                    },
                    "required":["banco","categoria","cuenta","motivo"],
                    "additionalProperties":false
                  }
                }
              },
              "required":["grupos","orientaciones"],
              "additionalProperties":false
            }
          }
        }
        """);
}
