namespace McpGateway.BddTests.Support;

public static class OpenApiSpecs
{
    public const string TwelveOperations = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Twelve-Op API", "version": "1.0.0" },
      "paths": {
        "/users": {
          "get": { "operationId": "listUsers", "summary": "List users", "responses": { "200": { "description": "OK" } } },
          "post": { "operationId": "createUser", "summary": "Create user", "requestBody": { "content": { "application/json": { "schema": { "type": "object", "properties": { "id": { "type": "string" }, "name": { "type": "string" } }, "required": ["id", "name"] } } } }, "responses": { "201": { "description": "Created" } } }
        },
        "/users/{id}": {
          "get": { "operationId": "getUser", "summary": "Get user", "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ], "responses": { "200": { "description": "OK" } } },
          "put": { "operationId": "replaceUser", "summary": "Replace user", "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ], "responses": { "200": { "description": "OK" } } },
          "patch": { "operationId": "patchUser", "summary": "Patch user", "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ], "responses": { "200": { "description": "OK" } } },
          "delete": { "operationId": "deleteUser", "summary": "Delete user", "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ], "responses": { "204": { "description": "No content" } } }
        },
        "/orders": { "get": { "operationId": "listOrders", "summary": "List orders", "responses": { "200": { "description": "OK" } } } },
        "/orders/{id}": { "get": { "operationId": "getOrder", "summary": "Get order", "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ], "responses": { "200": { "description": "OK" } } } },
        "/products": { "get": { "operationId": "listProducts", "summary": "List products", "responses": { "200": { "description": "OK" } } } },
        "/invoices": { "get": { "operationId": "listInvoices", "summary": "List invoices", "parameters": [ { "name": "limit", "in": "query", "schema": { "type": "integer" } }, { "name": "offset", "in": "query", "schema": { "type": "integer" } } ], "responses": { "200": { "description": "OK" } } } },
        "/reports": { "get": { "operationId": "getReport", "summary": "Get report", "responses": { "200": { "description": "OK" } } } },
        "/health": { "get": { "operationId": "getHealth", "summary": "Health", "responses": { "200": { "description": "OK" } } } }
      },
      "components": {
        "schemas": {
          "User": { "type": "object", "properties": { "id": { "type": "string" }, "name": { "type": "string" } }, "required": ["id", "name"] }
        }
      }
    }
    """;

    public const string SingleOperationWithRef = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Ref API", "version": "1.0.0" },
      "paths": {
        "/widgets": { "get": { "operationId": "listWidgets", "summary": "List widgets", "responses": { "200": { "description": "OK", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Widget" } } } } } } }
      },
      "components": {
        "schemas": {
          "Widget": { "type": "object", "properties": { "id": { "type": "integer" }, "label": { "type": "string" } } }
        }
      }
    }
    """;

    public const string SingleOperationNoRef = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Inline API", "version": "1.0.0" },
      "paths": {
        "/metrics": { "get": { "operationId": "getMetrics", "summary": "Get metrics", "responses": { "200": { "description": "OK" } } } }
      }
    }
    """;

    public const string TwoOperations = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Two-Op API", "version": "1.0.0" },
      "paths": {
        "/items": { "get": { "operationId": "listItems", "summary": "List items", "responses": { "200": { "description": "OK" } } } },
        "/items/{id}": { "get": { "operationId": "getItem", "summary": "Get item", "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ], "responses": { "200": { "description": "OK" } } } }
      }
    }
    """;

    public const string ThreeOperations = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Three-Op API", "version": "1.0.0" },
      "paths": {
        "/items": {
          "get": { "operationId": "listItems", "summary": "List items", "responses": { "200": { "description": "OK" } } },
          "post": { "operationId": "createItem", "summary": "Create item", "requestBody": { "content": { "application/json": { "schema": { "type": "object", "properties": { "id": { "type": "integer" } } } } } }, "responses": { "201": { "description": "Created" } } }
        },
        "/items/{id}": { "get": { "operationId": "getItem", "summary": "Get item", "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ], "responses": { "200": { "description": "OK" } } } }
      }
    }
    """;

    public const string SingleOperationWithRefAndAnyOf = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Universal API", "version": "1.0.0" },
      "paths": {
        "/widgets": {
          "get": {
            "operationId": "listWidgets",
            "summary": "List widgets",
            "parameters": [
              { "name": "filter", "in": "query", "schema": { "$ref": "#/components/schemas/Filter" } }
            ],
            "responses": {
              "200": {
                "description": "OK",
                "content": {
                  "application/json": {
                    "schema": {
                      "anyOf": [
                        { "type": "string" },
                        { "type": "integer" }
                      ]
                    }
                  }
                }
              }
            }
          }
        }
      },
      "components": {
        "schemas": {
          "Filter": { "type": "object", "properties": { "name": { "type": "string" } } }
        }
      }
    }
    """;

    public const string OperationWithAnyOf = """
    {
      "openapi": "3.0.0",
      "info": { "title": "AnyOf API", "version": "1.0.0" },
      "paths": {
        "/things": {
          "get": {
            "operationId": "listThings",
            "summary": "List things",
            "responses": {
              "200": {
                "description": "OK",
                "content": {
                  "application/json": {
                    "schema": {
                      "anyOf": [
                        { "type": "string" },
                        { "type": "integer" }
                      ]
                    }
                  }
                }
              }
            }
          }
        }
      }
    }
    """;

    public const string OperationWithLongTitle = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Long Title API", "version": "1.0.0" },
      "paths": {
        "/widgets": {
          "get": {
            "operationId": "listWidgets",
            "summary": "List widgets",
            "responses": { "200": { "description": "OK", "content": { "application/json": { "schema": { "type": "object", "title": "ThisIsAVeryLongTitleThatExceedsTheSixtyCharacterLimitForCursorProfiles", "properties": { "id": { "type": "integer" } } } } } } }
          }
        }
      }
    }
    """;

    public const string TwoOperationsWithExtraEndpoint = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Refresh API", "version": "1.0.0" },
      "paths": {
        "/items": { "get": { "operationId": "listItems", "summary": "List items", "responses": { "200": { "description": "OK" } } } },
        "/items/{id}": { "get": { "operationId": "getItem", "summary": "Get item", "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ], "responses": { "200": { "description": "OK" } } } },
        "/gadgets": { "get": { "operationId": "listGadgets", "summary": "List gadgets", "responses": { "200": { "description": "OK" } } } }
      }
    }
    """;

    public const string TwoOperationsWithDifferentEndpoint = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Refresh API", "version": "1.0.0" },
      "paths": {
        "/widgets": { "get": { "operationId": "listWidgets", "summary": "List widgets", "responses": { "200": { "description": "OK" } } } },
        "/sprockets": { "get": { "operationId": "listSprockets", "summary": "List sprockets", "responses": { "200": { "description": "OK" } } } }
      }
    }
    """;
}
