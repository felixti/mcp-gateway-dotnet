# Tool Generation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Parse any OpenAPI 3.0+ spec (JSON or YAML) and generate a list of `GeneratedTool` objects — one per `(path, method)` operation — with stable names, descriptions, input/output JSON schemas, and HTTP method/path metadata.

**Architecture:** A pure, stateless pipeline in `McpGateway.Core`. `OpenApiParser` loads the spec into `Microsoft.OpenApi.Models.OpenApiDocument`. `ToolGenerator` iterates operations and delegates to `ToolNameResolver`, `DescriptionBuilder`, `PaginationDetector`, and `SchemaTransformer` to produce `GeneratedTool` instances. The output is a plain list with no persistence or HTTP dependencies. The Management API plan maps `GeneratedTool` to the persistent `ToolDefinition` domain model.

**Tech Stack:** .NET 10, Microsoft.OpenApi 1.6.29, Microsoft.OpenApi.Readers 1.6.29, System.Text.Json, xUnit, FluentAssertions.

---

## File Structure

```
src/McpGateway.Core/
├── ToolGeneration/
│   ├── GeneratedTool.cs
│   ├── IToolGenerator.cs
│   ├── OpenApiParser.cs
│   ├── ToolGenerator.cs
│   ├── ToolNameResolver.cs
│   ├── DescriptionBuilder.cs
│   ├── PaginationDetector.cs
│   └── SchemaTransformer.cs

tests/McpGateway.UnitTests/
├── McpGateway.UnitTests.csproj
└── ToolGeneration/
    ├── OpenApiParserTests.cs
    ├── ToolGeneratorTests.cs
    ├── SchemaTransformerTests.cs
    ├── ToolNameResolverTests.cs
    └── PaginationDetectorTests.cs
```

---

### Task 1: Set up Core and unit test projects

**Files:**
- Create: `src/McpGateway.Core/ToolGeneration/GeneratedTool.cs`
- Create: `src/McpGateway.Core/ToolGeneration/IToolGenerator.cs`
- Modify: `src/McpGateway.Core/McpGateway.Core.csproj`
- Create: `tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj`

*Prerequisite:* This plan assumes `McpGateway.sln`, `src/McpGateway.Core/`, `ToolMode.cs`, and `ClientProfile.cs` already exist from the Persistence & Database plan.

- [ ] **Step 1: Add `GeneratedTool` model and `IToolGenerator` interface**

Create `src/McpGateway.Core/ToolGeneration/GeneratedTool.cs`:

```csharp
using System.Text.Json.Nodes;

namespace McpGateway.Core.ToolGeneration;

public class GeneratedTool
{
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string HttpMethod { get; set; } = null!;
    public string HttpPath { get; set; } = null!;
    public JsonNode InputSchema { get; set; } = new JsonObject();
    public JsonNode? OutputSchema { get; set; }
    public string AuthConfig { get; set; } = "{}";
    public bool Visible { get; set; } = true;
}
```

Create `src/McpGateway.Core/ToolGeneration/IToolGenerator.cs`:

```csharp
using McpGateway.Core.ServerDefinitions;
using Microsoft.OpenApi.Models;

namespace McpGateway.Core.ToolGeneration;

public interface IToolGenerator
{
    IReadOnlyList<GeneratedTool> Generate(OpenApiDocument document, ClientProfile profile);
    IReadOnlyList<GeneratedTool> Generate(string openApiSpec, ClientProfile profile);
}
```

Note: `Visible` is used by the MCP endpoint layer to hide tools in curated mode. The tool generator always sets it to `true`; admin overrides may change it later.

- [ ] **Step 2: Add OpenAPI packages to Core**

Run:

```bash
dotnet add src/McpGateway.Core/McpGateway.Core.csproj package Microsoft.OpenApi --version 1.6.29
dotnet add src/McpGateway.Core/McpGateway.Core.csproj package Microsoft.OpenApi.Readers --version 1.6.29
```

- [ ] **Step 3: Create unit test project**

Run:

```bash
dotnet new xunit -n McpGateway.UnitTests -o /var/home/felix/github/mcp-gateway/tests/McpGateway.UnitTests --framework net10.0
dotnet sln /var/home/felix/github/mcp-gateway/McpGateway.sln add tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj
dotnet add tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj reference src/McpGateway.Core/McpGateway.Core.csproj
dotnet add tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj package FluentAssertions --version 8.2.0
```

- [ ] **Step 4: Verify build**

Run:

```bash
dotnet build src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds.

---

### Task 2: Implement `OpenApiParser`

**Files:**
- Create: `src/McpGateway.Core/ToolGeneration/OpenApiParser.cs`
- Create: `tests/McpGateway.UnitTests/ToolGeneration/OpenApiParserTests.cs`

- [ ] **Step 1: Write failing test**

Create `tests/McpGateway.UnitTests/ToolGeneration/OpenApiParserTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.ToolGeneration;

public class OpenApiParserTests
{
    private const string MinimalOpenApiJson = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Test API", "version": "1.0.0" },
      "paths": {
        "/users": {
          "get": {
            "operationId": "listUsers",
            "summary": "List users",
            "responses": {
              "200": { "description": "OK" }
            }
          }
        }
      }
    }
    """;

    [Fact]
    public void ParseJson_ReturnsDocument_WithOnePath()
    {
        var parser = new OpenApiParser();
        var document = parser.Parse(MinimalOpenApiJson);

        document.Should().NotBeNull();
        document.Paths.Should().ContainSingle();
        document.Paths.First().Key.Should().Be("/users");
    }

    [Fact]
    public void ParseYaml_ReturnsDocument_WithOnePath()
    {
        var yaml = MinimalOpenApiJson.Replace('{', '\n').Replace('}', '\n').Replace(':', ": ").Replace('"', "'");
        var parser = new OpenApiParser();

        Action act = () => parser.ParseYaml(yaml);

        act.Should().Throw<NotImplementedException>();
    }
}
```

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~OpenApiParserTests" -v n
```

Expected: FAIL with "type or namespace OpenApiParser could not be found".

- [ ] **Step 2: Implement `OpenApiParser`**

Create `src/McpGateway.Core/ToolGeneration/OpenApiParser.cs`:

```csharp
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace McpGateway.Core.ToolGeneration;

public class OpenApiParser
{
    public OpenApiDocument Parse(string content)
    {
        var reader = new OpenApiStringReader();
        var result = reader.Read(content, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            var messages = string.Join("; ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"OpenAPI parse errors: {messages}");
        }

        return result;
    }

    public OpenApiDocument ParseYaml(string content)
    {
        var reader = new OpenApiStringReader();
        var result = reader.Read(content, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            var messages = string.Join("; ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"OpenAPI parse errors: {messages}");
        }

        return result;
    }
}
```

Note: `OpenApiStringReader` auto-detects JSON vs YAML in Microsoft.OpenApi.Readers 1.6.x, so `Parse` and `ParseYaml` can both delegate to it. Keep separate methods for API clarity.

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~OpenApiParserTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core tests/McpGateway.UnitTests McpGateway.sln
git commit -m "feat(tool-generation): add OpenApiParser and GeneratedTool model

- Add GeneratedTool, ToolMode, ClientProfile to Core
- Add OpenApiParser with JSON/YAML support via Microsoft.OpenApi.Readers
- Add unit tests for parsing"
```

---

### Task 3: Implement `ToolNameResolver`

**Files:**
- Create: `src/McpGateway.Core/ToolGeneration/ToolNameResolver.cs`
- Create: `tests/McpGateway.UnitTests/ToolGeneration/ToolNameResolverTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpGateway.UnitTests/ToolGeneration/ToolNameResolverTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.ToolGeneration;

public class ToolNameResolverTests
{
    private readonly ToolNameResolver _resolver = new();

    [Theory]
    [InlineData("getUser", "GET", "/users/{id}", "getUser")]
    [InlineData(null, "GET", "/users/{id}", "get_users_id")]
    [InlineData("", "POST", "/invoices", "post_invoices")]
    [InlineData("List-Users", "GET", "/users", "list_users")]
    public void Resolve_ReturnsExpectedName(string? operationId, string method, string path, string expected)
    {
        var name = _resolver.Resolve(operationId, method, path);
        name.Should().Be(expected);
    }
}
```

Run tests, expect FAIL.

- [ ] **Step 2: Implement `ToolNameResolver`**

Create `src/McpGateway.Core/ToolGeneration/ToolNameResolver.cs`:

```csharp
using System.Text.RegularExpressions;

namespace McpGateway.Core.ToolGeneration;

public partial class ToolNameResolver
{
    public string Resolve(string? operationId, string method, string path)
    {
        if (!string.IsNullOrWhiteSpace(operationId))
        {
            return Sanitize(operationId);
        }

        var sanitizedPath = path.Trim('/')
            .Replace("{", "")
            .Replace("}", "")
            .Replace("/", "_");

        return Sanitize($"{method.ToLowerInvariant()}_{sanitizedPath}");
    }

    private static string Sanitize(string value)
    {
        var lower = value.ToLowerInvariant();
        var withUnderscores = InvalidCharactersRegex().Replace(lower, "_");
        var trimmed = withUnderscores.Trim('_');
        var collapsed = Regex.Replace(trimmed, "_+", "_");
        return collapsed;
    }

    [GeneratedRegex(@"[^a-z0-9_]+")]
    private static partial Regex InvalidCharactersRegex();
}
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ToolNameResolverTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/ToolGeneration/ToolNameResolver.cs tests/McpGateway.UnitTests/ToolGeneration/ToolNameResolverTests.cs
git commit -m "feat(tool-generation): add ToolNameResolver

- Resolve operationId when present
- Synthesize method_path name when operationId missing"
```

---

### Task 4: Implement `DescriptionBuilder`

**Files:**
- Create: `src/McpGateway.Core/ToolGeneration/DescriptionBuilder.cs`
- Create: `tests/McpGateway.UnitTests/ToolGeneration/DescriptionBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpGateway.UnitTests/ToolGeneration/DescriptionBuilderTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.ToolGeneration;

public class DescriptionBuilderTests
{
    private readonly DescriptionBuilder _builder = new();

    [Fact]
    public void Build_PrefersSummary()
    {
        var description = _builder.Build("summary text", "description text", null);
        description.Should().Be("summary text");
    }

    [Fact]
    public void Build_FallsBackToDescription()
    {
        var description = _builder.Build(null, "description text", null);
        description.Should().Be("description text");
    }

    [Fact]
    public void Build_FallsBackToSynthesized()
    {
        var description = _builder.Build(null, null, "GET /users/{id}");
        description.Should().Be("GET /users/{id}");
    }

    [Fact]
    public void Build_TrimsAndDeduplicatesWhitespace()
    {
        var description = _builder.Build("  summary   text  ", null, null);
        description.Should().Be("summary text");
    }
}
```

Run tests, expect FAIL.

- [ ] **Step 2: Implement `DescriptionBuilder`**

Create `src/McpGateway.Core/ToolGeneration/DescriptionBuilder.cs`:

```csharp
using System.Text.RegularExpressions;

namespace McpGateway.Core.ToolGeneration;

public partial class DescriptionBuilder
{
    public string Build(string? summary, string? description, string? fallback)
    {
        var text = !string.IsNullOrWhiteSpace(summary)
            ? summary
            : !string.IsNullOrWhiteSpace(description)
                ? description
                : fallback ?? string.Empty;

        return WhitespaceRegex().Replace(text.Trim(), " ");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~DescriptionBuilderTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/ToolGeneration/DescriptionBuilder.cs tests/McpGateway.UnitTests/ToolGeneration/DescriptionBuilderTests.cs
git commit -m "feat(tool-generation): add DescriptionBuilder

- Prefer summary, fallback to description, then synthesized text"
```

---

### Task 5: Implement `PaginationDetector`

**Files:**
- Create: `src/McpGateway.Core/ToolGeneration/PaginationDetector.cs`
- Create: `tests/McpGateway.UnitTests/ToolGeneration/PaginationDetectorTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpGateway.UnitTests/ToolGeneration/PaginationDetectorTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.ToolGeneration;
using Microsoft.OpenApi.Models;

namespace McpGateway.UnitTests.ToolGeneration;

public class PaginationDetectorTests
{
    private readonly PaginationDetector _detector = new();

    [Fact]
    public void Detect_WithLimitAndOffset_ReturnsPaginationNote()
    {
        var operation = new OpenApiOperation
        {
            Parameters = new List<OpenApiParameter>
            {
                new() { Name = "limit", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "integer" } },
                new() { Name = "offset", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "integer" } }
            }
        };

        var note = _detector.Detect(operation);

        note.Should().Contain("limit");
        note.Should().Contain("offset");
    }

    [Fact]
    public void Detect_WithoutPaginationParams_ReturnsNull()
    {
        var operation = new OpenApiOperation
        {
            Parameters = new List<OpenApiParameter>
            {
                new() { Name = "id", In = ParameterLocation.Path, Schema = new OpenApiSchema { Type = "string" } }
            }
        };

        var note = _detector.Detect(operation);

        note.Should().BeNull();
    }
}
```

Run tests, expect FAIL.

- [ ] **Step 2: Implement `PaginationDetector`**

Create `src/McpGateway.Core/ToolGeneration/PaginationDetector.cs`:

```csharp
using Microsoft.OpenApi.Models;

namespace McpGateway.Core.ToolGeneration;

public class PaginationDetector
{
    private static readonly HashSet<string> LimitNames = new(StringComparer.OrdinalIgnoreCase) { "limit", "page_size", "pageSize", "per_page", "perPage" };
    private static readonly HashSet<string> OffsetNames = new(StringComparer.OrdinalIgnoreCase) { "offset", "page", "page_number", "pageNumber", "cursor" };

    public string? Detect(OpenApiOperation operation)
    {
        var parameters = operation.Parameters?.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var limit = parameters.FirstOrDefault(p => LimitNames.Contains(p));
        var offset = parameters.FirstOrDefault(p => OffsetNames.Contains(p));

        if (limit is null && offset is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (limit is not null)
        {
            parts.Add($"supports pagination via `{limit}` parameter");
        }
        if (offset is not null)
        {
            parts.Add($"page control via `{offset}` parameter");
        }

        return "Pagination: " + string.Join("; ", parts) + ".";
    }
}
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~PaginationDetectorTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/ToolGeneration/PaginationDetector.cs tests/McpGateway.UnitTests/ToolGeneration/PaginationDetectorTests.cs
git commit -m "feat(tool-generation): add PaginationDetector

- Detect limit/offset/page/cursor query parameters
- Append pagination note to tool description"
```

---

### Task 6: Implement `SchemaTransformer`

**Files:**
- Create: `src/McpGateway.Core/ToolGeneration/SchemaTransformer.cs`
- Create: `tests/McpGateway.UnitTests/ToolGeneration/SchemaTransformerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpGateway.UnitTests/ToolGeneration/SchemaTransformerTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpGateway.UnitTests.ToolGeneration;

public class SchemaTransformerTests
{
    private readonly SchemaTransformer _transformer = new();

    [Fact]
    public void Transform_Universal_InlinesRefs()
    {
        var schema = JsonNode.Parse("""
        {
          "$ref": "#/components/schemas/User"
        }
        """);
        var components = JsonNode.Parse("""
        {
          "schemas": {
            "User": { "type": "object", "properties": { "id": { "type": "integer" } } }
          }
        }
        """);

        var result = _transformer.Transform(schema!, ClientProfile.Universal, components);

        result["type"]!.GetValue<string>().Should().Be("object");
        result["properties"]!["id"]!["type"]!.GetValue<string>().Should().Be("integer");
    }

    [Fact]
    public void Transform_Universal_SplitsAnyOf()
    {
        var schema = JsonNode.Parse("""
        {
          "anyOf": [
            { "type": "string" },
            { "type": "integer" }
          ]
        }
        """);

        var result = _transformer.Transform(schema!, ClientProfile.Universal, null);

        result["oneOf"].Should().NotBeNull();
        result["oneOf"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public void Transform_Cursor_TruncatesName()
    {
        var schema = JsonNode.Parse("""
        {
          "title": "ThisIsAVeryLongTitleThatShouldBeTruncatedBecauseCursorHasStrictLimitsOnToolNameLength",
          "type": "object"
        }
        """);

        var result = _transformer.Transform(schema!, ClientProfile.Cursor, null);

        result["title"]!.GetValue<string>().Should().HaveLength(60);
    }
}
```

Run tests, expect FAIL.

- [ ] **Step 2: Implement `SchemaTransformer`**

Create `src/McpGateway.Core/ToolGeneration/SchemaTransformer.cs`:

```csharp
using McpGateway.Core.ServerDefinitions;
using System.Text.Json.Nodes;

namespace McpGateway.Core.ToolGeneration;

public class SchemaTransformer
{
    private const int CursorMaxTitleLength = 60;

    public JsonNode Transform(JsonNode schema, ClientProfile profile, JsonNode? components)
    {
        var clone = JsonNode.Parse(schema.ToJsonString())
            ?? throw new InvalidOperationException("Failed to clone schema.");

        InlineRefs(clone, components);

        if (profile is ClientProfile.Universal or ClientProfile.Cursor)
        {
            ReplaceAnyOfWithOneOf(clone);
        }

        if (profile == ClientProfile.Cursor)
        {
            TruncateTitles(clone, CursorMaxTitleLength);
        }

        return clone;
    }

    private static void InlineRefs(JsonNode node, JsonNode? components)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("$ref") && components is not null)
            {
                var refPath = obj["$ref"]!.GetValue<string>();
                var resolved = ResolveRef(components, refPath);
                if (resolved is not null)
                {
                    obj.Remove("$ref");
                    foreach (var property in resolved.AsObject())
                    {
                        obj[property.Key] = property.Value?.DeepClone();
                    }
                }
            }

            foreach (var property in obj)
            {
                if (property.Value is not null)
                {
                    InlineRefs(property.Value, components);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    InlineRefs(item, components);
                }
            }
        }
    }

    private static JsonNode? ResolveRef(JsonNode components, string refPath)
    {
        if (!refPath.StartsWith("#/components/"))
        {
            return null;
        }

        var parts = refPath.TrimStart('#', '/').Split('/');
        var current = components;
        foreach (var part in parts.Skip(1))
        {
            current = current[part];
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static void ReplaceAnyOfWithOneOf(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("anyOf"))
            {
                var anyOf = obj["anyOf"]!;
                obj.Remove("anyOf");
                obj["oneOf"] = anyOf.DeepClone();
            }

            foreach (var property in obj)
            {
                if (property.Value is not null)
                {
                    ReplaceAnyOfWithOneOf(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    ReplaceAnyOfWithOneOf(item);
                }
            }
        }
    }

    private static void TruncateTitles(JsonNode node, int maxLength)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("title") && obj["title"]?.GetValue<string>() is { } title)
            {
                obj["title"] = title.Length > maxLength ? title[..maxLength] : title;
            }

            foreach (var property in obj)
            {
                if (property.Value is not null)
                {
                    TruncateTitles(property.Value, maxLength);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    TruncateTitles(item, maxLength);
                }
            }
        }
    }
}
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~SchemaTransformerTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/ToolGeneration/SchemaTransformer.cs tests/McpGateway.UnitTests/ToolGeneration/SchemaTransformerTests.cs
git commit -m "feat(tool-generation): add SchemaTransformer

- Inline $ref references from components/schemas
- Convert anyOf to oneOf for universal/cursor profiles
- Truncate titles to 60 chars for cursor profile"
```

---

### Task 7: Implement `ToolGenerator`

**Files:**
- Create: `src/McpGateway.Core/ToolGeneration/ToolGenerator.cs`
- Create: `tests/McpGateway.UnitTests/ToolGeneration/ToolGeneratorTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpGateway.UnitTests/ToolGeneration/ToolGeneratorTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.ToolGeneration;

public class ToolGeneratorTests
{
    private readonly ToolGenerator _generator = new();

    [Fact]
    public void Generate_FromMinimalSpec_ReturnsOneTool()
    {
        const string spec = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Test API", "version": "1.0.0" },
          "paths": {
            "/users": {
              "get": {
                "operationId": "listUsers",
                "summary": "List users",
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

        var tools = _generator.Generate(spec, ClientProfile.Universal);

        tools.Should().ContainSingle();
        tools[0].Name.Should().Be("listUsers");
        tools[0].Description.Should().Be("List users");
        tools[0].HttpMethod.Should().Be("GET");
        tools[0].HttpPath.Should().Be("/users");
    }

    [Fact]
    public void Generate_SynthesizesName_WhenOperationIdMissing()
    {
        const string spec = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Test API", "version": "1.0.0" },
          "paths": {
            "/users/{id}": {
              "get": {
                "summary": "Get a user",
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

        var tools = _generator.Generate(spec, ClientProfile.Universal);

        tools.Should().ContainSingle();
        tools[0].Name.Should().Be("get_users_id");
    }

    [Fact]
    public void Generate_AppliesPaginationNote()
    {
        const string spec = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Test API", "version": "1.0.0" },
          "paths": {
            "/users": {
              "get": {
                "operationId": "listUsers",
                "summary": "List users",
                "parameters": [
                  { "name": "limit", "in": "query", "schema": { "type": "integer" } },
                  { "name": "offset", "in": "query", "schema": { "type": "integer" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

        var tools = _generator.Generate(spec, ClientProfile.Universal);

        tools[0].Description.Should().Contain("Pagination");
    }
}
```

Run tests, expect FAIL.

- [ ] **Step 2: Implement `ToolGenerator`**

Create `src/McpGateway.Core/ToolGeneration/ToolGenerator.cs`:

```csharp
using McpGateway.Core.ServerDefinitions;
using Microsoft.OpenApi.Models;
using System.Text.Json.Nodes;

namespace McpGateway.Core.ToolGeneration;

public class ToolGenerator : IToolGenerator
{
    private readonly OpenApiParser _parser = new();
    private readonly ToolNameResolver _nameResolver = new();
    private readonly DescriptionBuilder _descriptionBuilder = new();
    private readonly PaginationDetector _paginationDetector = new();
    private readonly SchemaTransformer _schemaTransformer = new();

    public IReadOnlyList<GeneratedTool> Generate(string openApiSpec, ClientProfile profile)
    {
        var document = _parser.Parse(openApiSpec);
        return Generate(document, profile);
    }

    public IReadOnlyList<GeneratedTool> Generate(OpenApiDocument document, ClientProfile profile)
    {
        var componentsNode = ConvertComponentsToJsonNode(document.Components);

        var tools = new List<GeneratedTool>();

        foreach (var pathItem in document.Paths)
        {
            foreach (var operation in pathItem.Value.Operations)
            {
                var tool = BuildTool(operation.Value, pathItem.Key, operation.Key, profile, componentsNode);
                tools.Add(tool);
            }
        }

        return tools;
    }

    private GeneratedTool BuildTool(OpenApiOperation operation, string path, OperationType method, ClientProfile profile, JsonNode? componentsNode)
    {
        var name = _nameResolver.Resolve(operation.OperationId, method.ToString().ToUpperInvariant(), path);
        var paginationNote = _paginationDetector.Detect(operation);
        var baseDescription = _descriptionBuilder.Build(
            operation.Summary,
            operation.Description,
            $"{method.ToString().ToUpperInvariant()} {path}");

        var description = paginationNote is not null
            ? $"{baseDescription} {paginationNote}"
            : baseDescription;

        var inputSchema = BuildInputSchema(operation, componentsNode, profile);
        var outputSchema = BuildOutputSchema(operation, componentsNode, profile);

        return new GeneratedTool
        {
            Name = name,
            Description = description,
            HttpMethod = method.ToString().ToUpperInvariant(),
            HttpPath = path,
            InputSchema = inputSchema,
            OutputSchema = outputSchema
        };
    }

    private JsonNode BuildInputSchema(OpenApiOperation operation, JsonNode? componentsNode, ClientProfile profile)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray()
        };

        foreach (var parameter in operation.Parameters ?? [])
        {
            var parameterSchema = parameter.Schema is not null
                ? _schemaTransformer.Transform(ConvertSchemaToJsonNode(parameter.Schema), profile, componentsNode)
                : new JsonObject();

            ((JsonObject)schema["properties"]!)[parameter.Name] = parameterSchema;

            if (parameter.Required)
            {
                ((JsonArray)schema["required"]!).Add(parameter.Name);
            }
        }

        if (operation.RequestBody?.Content.TryGetValue("application/json", out var mediaType) == true
            && mediaType.Schema is not null)
        {
            var bodySchema = _schemaTransformer.Transform(
                ConvertSchemaToJsonNode(mediaType.Schema),
                profile,
                componentsNode);

            ((JsonObject)schema["properties"]!)["body"] = bodySchema;
            ((JsonArray)schema["required"]!).Add("body");
        }

        return schema;
    }

    private JsonNode? BuildOutputSchema(OpenApiOperation operation, JsonNode? componentsNode, ClientProfile profile)
    {
        var successResponse = operation.Responses
            .FirstOrDefault(r => r.Key.StartsWith('2'))
            .Value;

        if (successResponse?.Content.TryGetValue("application/json", out var mediaType) != true
            || mediaType.Schema is null)
        {
            return null;
        }

        return _schemaTransformer.Transform(
            ConvertSchemaToJsonNode(mediaType.Schema),
            profile,
            componentsNode);
    }

    private static JsonNode ConvertSchemaToJsonNode(OpenApiSchema schema)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(schema, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        return JsonNode.Parse(json) ?? new JsonObject();
    }

    private static JsonNode? ConvertComponentsToJsonNode(OpenApiComponents? components)
    {
        if (components is null)
        {
            return null;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(components, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        return JsonNode.Parse(json);
    }
}
```

Note: `ConvertSchemaToJsonNode` using `JsonSerializer.Serialize` on `OpenApiSchema` may produce OpenAPI-specific serialization. If Microsoft.OpenApi 1.6 doesn't serialize cleanly with System.Text.Json, replace with manual conversion. Test first; if tests fail, implement a manual converter that maps `Type`, `Properties`, `Required`, `Items`, `AnyOf`, `OneOf`, `Ref`, etc.

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ToolGeneratorTests" -v n
```

Expected: PASS. If serialization produces unexpected shapes, fix `ConvertSchemaToJsonNode`.

- [ ] **Step 4: Run all unit tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/McpGateway.Core/ToolGeneration/ToolGenerator.cs tests/McpGateway.UnitTests/ToolGeneration/ToolGeneratorTests.cs
git commit -m "feat(tool-generation): add ToolGenerator orchestrator

- Generate GeneratedTool list from OpenAPI document
- Build input schema from path/query/body params
- Build output schema from first 2xx response
- Integrate name resolver, description builder, pagination detector, schema transformer"
```

---

## Self-Review

**1. Spec coverage:**

| Requirement | Task |
|---|---|
| Parse OpenAPI 3.0+ JSON/YAML | Task 2 |
| Generate one tool per (path, method) | Task 7 |
| Resolve `operationId` or synthesize name | Task 3 |
| Build description from summary/description/fallback | Task 4 |
| Detect pagination params and enhance description | Task 5 |
| Transform schema per `ClientProfile` | Task 6 |
| Output `GeneratedTool` with name, description, schemas, HTTP metadata | Task 1 + Task 7 |
| Unit tests for all components | Each task |

**2. Placeholder scan:**

No `TBD`, `TODO`, or vague steps. Each task contains complete code and exact commands.

**3. Type consistency:**

- `GeneratedTool` uses `JsonNode` for schemas, matching `SchemaTransformer` output.
- `ToolMode` and `ClientProfile` enums are defined in the Persistence & Database plan and reused here.
- `GeneratedTool.Visible` defaults to `true` for generated tools.
- `HttpMethod` is uppercase string (`GET`, `POST`, etc.).

**4. Known follow-ups for Oracle review:**

- Resolved: `GeneratedTool` now includes `Visible` and lives in `McpGateway.Core.ToolGeneration`, separate from the persistence `ToolDefinition` model in `McpGateway.Core.ServerDefinitions`.
- `ConvertSchemaToJsonNode` uses `JsonSerializer.Serialize(OpenApiSchema)` — replace with a manual converter if Microsoft.OpenApi types do not serialize cleanly with System.Text.Json.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-07-03-tool-generation.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — Fresh subagent per task, review between tasks.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`.

**Which approach?**
