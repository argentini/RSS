using System.Text.Json;

namespace Generator;

internal static class HydrationResponseFormat
{
    public static JsonElement Value { get; } = Create();

    private const string Definition = """
        {
          "type": "json_schema",
          "json_schema": {
            "name": "json_feed_item",
            "strict": true,
            "schema": {
              "type": "object",
              "properties": {
                "title": {
                  "type": "string",
                  "minLength": 1
                },
                "date_published": {
                  "type": "string",
                  "minLength": 1
                },
                "date_modified": {
                  "type": "string",
                  "minLength": 1
                },
                "authors": {
                  "type": "array",
                  "minItems": 1,
                  "items": {
                    "type": "object",
                    "properties": {
                      "name": {
                        "type": "string",
                        "minLength": 1
                      },
                      "url": {
                        "type": "string",
                        "minLength": 1
                      },
                      "avatar": {
                        "type": "string",
                        "minLength": 1
                      }
                    },
                    "minProperties": 1,
                    "additionalProperties": false
                  }
                },
                "content_html": {
                  "type": "string",
                  "minLength": 1
                },
                "summary": {
                  "type": "string"
                },
                "image": {
                  "type": "string",
                  "minLength": 1
                },
                "banner_image": {
                  "type": "string",
                  "minLength": 1
                },
                "language": {
                  "type": "string",
                  "minLength": 1
                },
                "tags": {
                  "type": "array",
                  "items": {
                    "type": "string"
                  }
                },
                "attachments": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "url": {
                        "type": "string",
                        "minLength": 1
                      },
                      "mime_type": {
                        "type": "string",
                        "minLength": 1
                      },
                      "title": {
                        "type": "string"
                      },
                      "size_in_bytes": {
                        "type": "integer",
                        "minimum": 0
                      },
                      "duration_in_seconds": {
                        "type": "number",
                        "minimum": 0
                      }
                    },
                    "required": [
                      "url",
                      "mime_type"
                    ],
                    "additionalProperties": false
                  }
                }
              },
              "required": [
                "title",
                "date_published",
                "content_html"
              ],
              "additionalProperties": false
            }
          }
        }
        """;

    private static JsonElement Create()
    {
        using var document = JsonDocument.Parse(Definition);
        return document.RootElement.Clone();
    }
}
