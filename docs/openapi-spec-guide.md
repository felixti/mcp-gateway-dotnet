# OpenAPI spec guide for MCP Gateway

This guide explains how to write an OpenAPI specification that the MCP Gateway can turn into high-quality MCP tools. Following these practices produces tools with clear names, accurate input schemas, and useful descriptions.

## What the gateway does with a spec

For every `(path, method)` pair in the spec, the gateway creates one MCP tool:

- **Tool name** — derived from `operationId`, or generated from the HTTP method and path.
- **Description** — built from `summary`, `description`, and detected pagination hints.
- **Input schema** — built from path parameters, query parameters, and the `application/json` request body.
- **Output schema** — built from the first `2xx` response that has an `application/json` schema.

The gateway then proxies `tools/call` to the upstream API using the base URL and auth strategy configured on the server definition.

## Supported OpenAPI versions and formats

- **OpenAPI 3.0.x** is the supported version. OpenAPI 2.0 (Swagger) may parse but is not explicitly tested.
- Specs can be provided as **JSON** or **YAML**.
- The spec must be syntactically valid; parse errors cause registration to fail.

## Tool naming

The gateway uses the first valid value it finds:

1. `operationId` on the operation (preferred).
2. Generated name: `{method}_{path}` with `{` and `}` removed and `/` replaced by `_`.

Examples:

| Path | Method | `operationId` | Generated tool name |
|---|---|---|---|
| `/pets` | `GET` | `listPets` | `listpets` |
| `/pets/{petId}` | `GET` | `getPetById` | `getpetbyid` |
| `/pets/{petId}` | `GET` | *(missing)* | `get_pets_petid` |

**Best practice:** Always set a unique, descriptive `operationId` for every operation. This makes tool names stable across spec refreshes and easier for MCP clients to call.

## Tool descriptions

The description is assembled from:

1. `summary`
2. `description` (if summary is absent)
3. `"{METHOD} {path}"` (fallback)

If the operation has pagination-related parameters, a note is appended:

> `Pagination: supports pagination via "limit" parameter; page control via "page" parameter.`

**Best practice:** Write concise `summary` fields (one sentence) and use `description` for behavior details, edge cases, and examples. Avoid leaving both blank.

## Input schema generation

The input schema is a JSON Schema object whose top-level `properties` contain:

### Path parameters

Path parameters such as `/pets/{petId}` become top-level properties named `petId`. They are required by default because the URL cannot be built without them.

### Query parameters

Query parameters become top-level properties. Only `Required = true` parameters are added to the `required` array.

### Request body

Only `application/json` request bodies are supported. The body schema is wrapped under a top-level `body` property and is always marked as required when present.

**Best practice:**

- Prefer query parameters for filters, pagination, and simple lookups.
- Use JSON request bodies for create/update operations.
- Mark required parameters explicitly.

### Example

```yaml
paths:
  /pets/{petId}:
    get:
      operationId: getPetById
      summary: Get a pet by its unique identifier
      parameters:
        - name: petId
          in: path
          required: true
          schema:
            type: integer
            format: int64
      responses:
        '200':
          description: Pet found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Pet'
```

Generated input schema:

```json
{
  "type": "object",
  "properties": {
    "petId": { "type": "integer", "format": "int64" }
  },
  "required": ["petId"]
}
```

## Output schema generation

The gateway picks the first response whose key starts with `2` (for example `200`, `201`, `204`). It then looks for an `application/json` schema.

If no `2xx` JSON schema exists, the tool has no output schema. The upstream response is still returned as text.

**Best practice:**

- Define a `200` or `201` response with an `application/json` schema for every operation.
- Keep response schemas close to the operation; `$ref` to `#/components/schemas/` is supported and inlined.

## Schema references and components

- `$ref` pointing to `#/components/schemas/{name}` is resolved and inlined into the tool schema.
- External references (`https://...` or other files) are **not** resolved and are left as-is, which usually produces invalid input/output schemas.
- Recursive references are inlined once; deeply cyclic schemas may expand.

**Best practice:** Keep all reusable schemas under `#/components/schemas/` and avoid external `$ref`.

## Client profiles

The gateway supports three client profiles on a server definition:

| Profile | Behavior |
|---|---|
| `universal` | Default. Inlines `$ref`, replaces `anyOf` with `oneOf`. |
| `cursor` | Same as `universal`, plus `title` values are truncated to 60 characters. |
| `claude` | Inlines `$ref` only; keeps `anyOf` unchanged. |

**Best practice:** Use `universal` unless a specific MCP client has known schema limitations. Use `cursor` if you see schema-size errors in Cursor.

## Pagination hints

The gateway detects common pagination parameter names and appends a note to the tool description. Detected names are:

- **Limit/page size:** `limit`, `page_size`, `pageSize`, `per_page`, `perPage`
- **Offset/page:** `offset`, `page`, `page_number`, `pageNumber`, `cursor`

**Best practice:** Use these exact parameter names so the generated tool description tells the MCP client that pagination is available.

## Best practices checklist

Use this checklist before registering a spec:

- [ ] Spec is valid OpenAPI 3.0.x JSON or YAML.
- [ ] Every operation has a unique `operationId`.
- [ ] Every operation has a `summary` or `description`.
- [ ] Path parameters are marked `required: true`.
- [ ] Query parameters that are required are marked `required: true`.
- [ ] Create/update operations use `application/json` request bodies.
- [ ] Successful responses (`200`/`201`) define an `application/json` schema.
- [ ] Reusable schemas live in `#/components/schemas/`.
- [ ] No external `$ref` values are used.
- [ ] Pagination parameters use the supported naming conventions.
- [ ] The spec contains no file-upload (`multipart/form-data`) or form-data (`application/x-www-form-urlencoded`) operations that need to be exposed as tools.

## Current limitations

The following OpenAPI features are not supported by the gateway today:

- **File uploads** via `multipart/form-data`.
- **Form data** via `application/x-www-form-urlencoded`.
- **Non-JSON responses** such as `text/plain`, `text/csv`, or binary downloads.
- **Callbacks and webhooks**.
- **External `$ref`** references.
- **Multiple request body media types** — only `application/json` is used.
- **Response content negotiation** — only the first `2xx` JSON response is used.
- **Authentication metadata** in the spec is parsed but auth execution is driven by the server definition's `authStrategy`, not by `securitySchemes`.

If a tool needs one of these features, expose it through a custom wrapper API and describe the wrapper in OpenAPI instead.

## Example minimal compliant spec

```yaml
openapi: 3.0.3
info:
  title: Pet Inventory API
  version: 1.0.0
paths:
  /pets:
    get:
      operationId: listPets
      summary: List pets in the inventory
      parameters:
        - name: status
          in: query
          schema:
            type: string
            enum: [available, pending, sold]
        - name: limit
          in: query
          schema:
            type: integer
            default: 20
      responses:
        '200':
          description: List of pets
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/Pet'
    post:
      operationId: createPet
      summary: Add a new pet
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/Pet'
      responses:
        '201':
          description: Pet created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Pet'
  /pets/{petId}:
    get:
      operationId: getPetById
      summary: Get a pet by ID
      parameters:
        - name: petId
          in: path
          required: true
          schema:
            type: integer
      responses:
        '200':
          description: Pet details
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Pet'
components:
  schemas:
    Pet:
      type: object
      required: [name]
      properties:
        id:
          type: integer
        name:
          type: string
        status:
          type: string
          enum: [available, pending, sold]
```

This spec produces three tools:

- `listpets` — query params `status`, `limit`; pagination hint in description.
- `createpet` — required `body` property matching `Pet`.
- `getpetbyid` — required `petId` path parameter.

## Registering the spec

Save the spec as JSON or YAML and register it through the admin API:

```bash
curl -X POST http://localhost:5121/admin/servers \
  -H "X-Dev-Admin: admin@example.com" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "pet-inventory",
    "displayName": "Pet Inventory",
    "specSourceUrl": "https://api.example.com/openapi.yaml",
    "baseUrl": "https://api.example.com",
    "authStrategy": "passthrough",
    "authConfig": {},
    "toolMode": "all",
    "clientProfile": "universal",
    "createdBy": "admin@example.com"
  }'
```

Then review the generated tools and approve the server definition.
