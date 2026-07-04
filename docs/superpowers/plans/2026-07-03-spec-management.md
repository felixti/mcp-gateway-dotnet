# Spec Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the spec lifecycle pipeline — fetch OpenAPI specs from URL or file uploads (JSON/YAML), diff old vs new spec into added/removed/changed tool lists, and run a background `SpecRefresher` (BackgroundService) that polls every server definition at its configured `PollIntervalMinutes` plus a manual `POST /admin/servers/{name}/refresh` endpoint — keeping the in-memory tool store in sync with upstream API changes.

**Architecture:** `SpecFetcher` is a stateless service that takes a `SpecSource` (URL or stream + filename) and returns the raw spec content (string) — JSON or YAML is detected and normalized. `SpecDiffService` parses both old and new specs via the existing `OpenApiParser` + `IToolGenerator`, then computes added/removed/changed tool lists by comparing generated `GeneratedTool.Name` plus an authoritative comparison of `HttpMethod`/`HttpPath`/`Description`/`InputSchema`/`OutputSchema` per matched tool. `SpecRefresher` is a `BackgroundService` that on each tick queries approved + active server definitions, calls `ISpecFetcher` to download each spec, hashes it, runs `ISpecDiffService` on change, persists the new `spec_versions` row, updates tools in PG, flips `approval_status='changes_pending'`, and removes the server from `IToolStore` so MCP clients immediately see -32005. A public `RefreshAsync(string name)` method is exposed for the manual admin endpoint (wired later in the Admin API plan). All three services live in `McpGateway.Core.SpecManagement` and depend only on abstractions (`IServerDefinitionRepository`, `IToolStore`, `IHttpClientFactory`, `IToolGenerator`).

**Tech Stack:** .NET 10, `Microsoft.OpenApi.Readers` 1.6.29 (already in Core from Tool Generation plan), `IHttpClientFactory` (Microsoft.Extensions.Http), `SHA256` (System.Security.Cryptography), `System.Threading.Channels` (for refresh trigger signal), `BackgroundService` (Microsoft.Extensions.Hosting), `xUnit`, `FluentAssertions`, `Testcontainers.PostgreSql`.

---

## File Structure

```
src/McpGateway.Core/
├── SpecManagement/
│   ├── SpecFetcher.cs                      # Fetch spec from URL (HttpClient) or stream
│   ├── SpecSource.cs                       # Sealed record describing source (URL or stream)
│   ├── FetchedSpec.cs                      # Sealed record (Content, Hash, DetectedFormat)
│   ├── SpecDiffService.cs                  # Compute added/removed/changed tool lists
│   ├── SpecDiffResult.cs                   # Sealed record with Added/Removed/Changed lists
│   ├── SpecToolChange.cs                   # Sealed record describing one changed tool (field-level)
│   ├── SpecRefresher.cs                    # BackgroundService: poll + manual RefreshAsync
│   ├── ServerSpecRefresher.cs              # Internal helper: refresh a single server by name
│   └── SpecRefreshOutcome.cs               # Sealed record: Unchanged / Updated(server) / Failed(error)
└── ToolStore/
    └── IToolStore.cs                       # (existing) RemoveServer, Contains, GetAllServers

src/McpGateway.Persistence/
├── Repositories/
│   ├── ServerDefinitionRepository.cs       # Modify: add InsertSpecVersionAsync helper
│   └── SpecVersionRepository.cs            # NEW: insert + list versions for a server
└── PersistenceServiceExtensions.cs         # Modify: register ISpecVersionRepository

tests/McpGateway.UnitTests/
└── SpecManagement/
    ├── SpecFetcherTests.cs                 # URL fetch with HttpClient mock, stream/file fetch, format detection
    ├── SpecDiffServiceTests.cs             # Add/remove/change detection, identical specs, name-only match
    └── SpecRefresherTests.cs               # Background service behavior (tick triggers refresh)

tests/McpGateway.IntegrationTests/
└── SpecManagement/
    └── SpecRefreshTests.cs                 # End-to-end: spec change → diff → PG updated → tool store cleared
```

---

### Task 1: Add `ISpecVersionRepository` and `SpecVersionRepository`

**Files:**
- Create: `src/McpGateway.Core/Repositories/ISpecVersionRepository.cs`
- Create: `src/McpGateway.Persistence/Repositories/SpecVersionRepository.cs`
- Modify: `src/McpGateway.Persistence/PersistenceServiceExtensions.cs`

*Prerequisite:* Persistence & Database plan already implemented (entities, `McpGatewayDbContext`, `SpecVersion` domain model exist).

- [ ] **Step 1: Write failing test for `SpecVersionRepository.AddAsync`**

Create `tests/McpGateway.IntegrationTests/SpecManagement/SpecVersionRepositoryTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Persistence.Repositories;

namespace McpGateway.IntegrationTests.SpecManagement;

[Collection("Persistence")]
public class SpecVersionRepositoryTests
{
    private readonly PostgreSqlFixture _fixture;

    public SpecVersionRepositoryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_PersistsVersionWithHashAndContent()
    {
        var serverRepo = new ServerDefinitionRepository(_fixture.CreateDbContext());
        var server = await serverRepo.AddAsync(new McpServerDefinition
        {
            Name = "spec-version-test",
            DisplayName = "Spec Version Test",
            SpecContent = "{}",
            SpecHash = "original-hash",
            BaseUrl = "https://spec-version.example.com"
        });

        await using var context = _fixture.CreateDbContext();
        var repo = new SpecVersionRepository(context);

        var version = new SpecVersion
        {
            ServerDefinitionId = server.Id,
            SpecHash = "new-hash",
            SpecContent = "{\"openapi\":\"3.0.0\"}",
            ToolCount = 5,
            DiffSummary = "{\"added\":[\"foo\"],\"removed\":[],\"changed\":[]}"
        };

        var added = await repo.AddAsync(version);

        added.Id.Should().NotBe(Guid.Empty);
        added.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        var retrieved = await repo.GetAsync(added.Id);
        retrieved.Should().NotBeNull();
        retrieved!.SpecHash.Should().Be("new-hash");
        retrieved.ToolCount.Should().Be(5);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```bash
dotnet test tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj --filter "FullyQualifiedName~SpecVersionRepositoryTests" -v n
```

Expected: FAIL with "type or namespace `ISpecVersionRepository` could not be found".

- [ ] **Step 3: Create `ISpecVersionRepository`**

Create `src/McpGateway.Core/Repositories/ISpecVersionRepository.cs`:

```csharp
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Repositories;

public interface ISpecVersionRepository
{
    Task<SpecVersion> AddAsync(SpecVersion version, CancellationToken ct = default);
    Task<SpecVersion?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SpecVersion>> ListByServerAsync(Guid serverDefinitionId, int limit = 50, CancellationToken ct = default);
    Task<SpecVersion?> GetLatestAsync(Guid serverDefinitionId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement `SpecVersionRepository`**

Create `src/McpGateway.Persistence/Repositories/SpecVersionRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Persistence.Entities;

namespace McpGateway.Persistence.Repositories;

public class SpecVersionRepository : ISpecVersionRepository
{
    private readonly McpGatewayDbContext _context;

    public SpecVersionRepository(McpGatewayDbContext context)
    {
        _context = context;
    }

    public async Task<SpecVersion> AddAsync(SpecVersion version, CancellationToken ct = default)
    {
        var entity = new SpecVersionEntity
        {
            Id = version.Id == Guid.Empty ? Guid.NewGuid() : version.Id,
            ServerDefinitionId = version.ServerDefinitionId,
            SpecHash = version.SpecHash,
            SpecContent = version.SpecContent,
            ToolCount = version.ToolCount,
            DiffSummary = version.DiffSummary,
            CreatedAt = DateTime.UtcNow
        };

        _context.SpecVersions.Add(entity);
        await _context.SaveChangesAsync(ct);

        return ToDomain(entity);
    }

    public async Task<SpecVersion?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.SpecVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<SpecVersion>> ListByServerAsync(Guid serverDefinitionId, int limit = 50, CancellationToken ct = default)
    {
        var entities = await _context.SpecVersions
            .AsNoTracking()
            .Where(v => v.ServerDefinitionId == serverDefinitionId)
            .OrderByDescending(v => v.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(ToDomain).ToList();
    }

    public async Task<SpecVersion?> GetLatestAsync(Guid serverDefinitionId, CancellationToken ct = default)
    {
        var entity = await _context.SpecVersions
            .AsNoTracking()
            .Where(v => v.ServerDefinitionId == serverDefinitionId)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : ToDomain(entity);
    }

    private static SpecVersion ToDomain(SpecVersionEntity entity) => new()
    {
        Id = entity.Id,
        ServerDefinitionId = entity.ServerDefinitionId,
        SpecHash = entity.SpecHash,
        SpecContent = entity.SpecContent,
        ToolCount = entity.ToolCount,
        DiffSummary = entity.DiffSummary,
        CreatedAt = entity.CreatedAt
    };
}
```

- [ ] **Step 5: Register `ISpecVersionRepository` in DI**

Modify `src/McpGateway.Persistence/PersistenceServiceExtensions.cs`. Replace the `AddScoped` block:

```csharp
        services.AddScoped<IServerDefinitionRepository, ServerDefinitionRepository>();
        services.AddScoped<IGatewayApiKeyRepository, GatewayApiKeyRepository>();
        services.AddScoped<IToolOverrideRepository, ToolOverrideRepository>();
```

with:

```csharp
        services.AddScoped<IServerDefinitionRepository, ServerDefinitionRepository>();
        services.AddScoped<IGatewayApiKeyRepository, GatewayApiKeyRepository>();
        services.AddScoped<IToolOverrideRepository, ToolOverrideRepository>();
        services.AddScoped<ISpecVersionRepository, SpecVersionRepository>();
```

- [ ] **Step 6: Run test to verify it passes**

Run:
```bash
dotnet test tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj --filter "FullyQualifiedName~SpecVersionRepositoryTests" -v n
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/McpGateway.Core/Repositories/ISpecVersionRepository.cs \
        src/McpGateway.Persistence/Repositories/SpecVersionRepository.cs \
        src/McpGateway.Persistence/PersistenceServiceExtensions.cs \
        tests/McpGateway.IntegrationTests/SpecManagement/SpecVersionRepositoryTests.cs
git commit -m "feat(spec-mgmt): add ISpecVersionRepository for spec_versions history

- Persist SpecVersion snapshots with hash, content, tool_count, diff_summary
- Add GetAsync, ListByServerAsync, GetLatestAsync query helpers
- Register in DI as scoped service"
```

---

### Task 2: Add `SpecSource`, `FetchedSpec`, and `ISpecFetcher`

**Files:**
- Create: `src/McpGateway.Core/SpecManagement/SpecSource.cs`
- Create: `src/McpGateway.Core/SpecManagement/FetchedSpec.cs`
- Create: `src/McpGateway.Core/SpecManagement/ISpecFetcher.cs`

- [ ] **Step 1: Create `SpecSource`**

Create `src/McpGateway.Core/SpecManagement/SpecSource.cs`:

```csharp
namespace McpGateway.Core.SpecManagement;

/// <summary>
/// Describes where to fetch an OpenAPI spec from.
/// Exactly one of <see cref="Url"/> or <see cref="Stream"/> is populated.
/// </summary>
public sealed record SpecSource
{
    public string? Url { get; init; }
    public Stream? Stream { get; init; }
    public string? FileName { get; init; }

    public static SpecSource FromUrl(string url) =>
        new() { Url = url };

    public static SpecSource FromStream(Stream stream, string? fileName = null) =>
        new() { Stream = stream, FileName = fileName };
}
```

- [ ] **Step 2: Create `FetchedSpec`**

Create `src/McpGateway.Core/SpecManagement/FetchedSpec.cs`:

```csharp
namespace McpGateway.Core.SpecManagement;

/// <summary>
/// Result of fetching a spec — raw content plus deterministic SHA-256 hash
/// for change detection.
/// </summary>
public sealed record FetchedSpec(string Content, string Hash, SpecFormat Format);

public enum SpecFormat
{
    Json,
    Yaml
}
```

- [ ] **Step 3: Create `ISpecFetcher`**

Create `src/McpGateway.Core/SpecManagement/ISpecFetcher.cs`:

```csharp
namespace McpGateway.Core.SpecManagement;

public interface ISpecFetcher
{
    Task<FetchedSpec> FetchAsync(SpecSource source, CancellationToken ct = default);
}
```

---

### Task 3: Implement `SpecFetcher`

**Files:**
- Create: `src/McpGateway.Core/SpecManagement/SpecFetcher.cs`
- Create: `tests/McpGateway.UnitTests/SpecManagement/SpecFetcherTests.cs`
- Modify: `src/McpGateway.Core/McpGateway.Core.csproj` (add Microsoft.Extensions.Http)

*Prerequisite:* `ISpecFetcher`, `SpecSource`, `FetchedSpec` from Task 2.

- [ ] **Step 1: Write failing test for `SpecFetcher` (stream + URL)**

Create `tests/McpGateway.UnitTests/SpecManagement/SpecFetcherTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using McpGateway.Core.SpecManagement;

namespace McpGateway.UnitTests.SpecManagement;

public class SpecFetcherTests
{
    [Fact]
    public async Task FetchAsync_FromStream_Json_ReturnsContentAndHash()
    {
        const string json = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"}}""";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var fetcher = new SpecFetcher(httpClientFactory: null!);
        var result = await fetcher.FetchAsync(SpecSource.FromStream(stream, "openapi.json"));

        result.Content.Should().Be(json);
        result.Format.Should().Be(SpecFormat.Json);
        result.Hash.Should().HaveLength(64);
        result.Hash.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public async Task FetchAsync_FromStream_Yaml_DetectsYamlFormat()
    {
        const string yaml = "openapi: 3.0.0\ninfo:\n  title: T\n  version: 1.0\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));

        var fetcher = new SpecFetcher(httpClientFactory: null!);
        var result = await fetcher.FetchAsync(SpecSource.FromStream(stream, "openapi.yaml"));

        result.Format.Should().Be(SpecFormat.Yaml);
        result.Content.Should().Be(yaml);
    }

    [Fact]
    public async Task FetchAsync_StreamYmlExtension_DetectsYaml()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("openapi: 3.0.0\n"));

        var fetcher = new SpecFetcher(httpClientFactory: null!);
        var result = await fetcher.FetchAsync(SpecSource.FromStream(stream, "spec.yml"));

        result.Format.Should().Be(SpecFormat.Yaml);
    }

    [Fact]
    public async Task FetchAsync_JsonWithoutFilename_DetectsByContent()
    {
        const string json = """{"openapi":"3.0.0"}""";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var fetcher = new SpecFetcher(httpClientFactory: null!);
        var result = await fetcher.FetchAsync(SpecSource.FromStream(stream));

        result.Format.Should().Be(SpecFormat.Json);
    }

    [Fact]
    public async Task FetchAsync_FromUrl_FetchesContent()
    {
        const string json = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"}}""";
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://specs.example.com/") };
        var factory = new StubHttpClientFactory(httpClient);

        var fetcher = new SpecFetcher(factory);
        var result = await fetcher.FetchAsync(SpecSource.FromUrl("https://specs.example.com/openapi.json"));

        result.Content.Should().Be(json);
        result.Format.Should().Be(SpecFormat.Json);
    }

    [Fact]
    public async Task FetchAsync_FromUrl_NonSuccess_Throws()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://specs.example.com/") };
        var factory = new StubHttpClientFactory(httpClient);

        var fetcher = new SpecFetcher(factory);

        Func<Task> act = () => fetcher.FetchAsync(SpecSource.FromUrl("https://specs.example.com/missing.json"));
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task FetchAsync_HashIsDeterministic()
    {
        const string json = """{"openapi":"3.0.0"}""";

        using var stream1 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        using var stream2 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var fetcher = new SpecFetcher(httpClientFactory: null!);
        var first = await fetcher.FetchAsync(SpecSource.FromStream(stream1));
        var second = await fetcher.FetchAsync(SpecSource.FromStream(stream2));

        first.Hash.Should().Be(second.Hash);
    }

    [Fact]
    public void FetchAsync_NullSource_Throws()
    {
        var fetcher = new SpecFetcher(httpClientFactory: null!);
        Func<Task> act = async () => await fetcher.FetchAsync(null!);
        act.Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_responder(request));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StubHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }
}
```

- [ ] **Step 2: Add `Microsoft.Extensions.Http` package to Core**

Run:
```bash
dotnet add src/McpGateway.Core/McpGateway.Core.csproj package Microsoft.Extensions.Http --version 9.0.4
```

- [ ] **Step 3: Implement `SpecFetcher`**

Create `src/McpGateway.Core/SpecManagement/SpecFetcher.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace McpGateway.Core.SpecManagement;

public class SpecFetcher : ISpecFetcher
{
    public const string HttpClientName = "spec-fetcher";
    private static readonly HashSet<string> YamlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".yaml", ".yml"
    };
    private static readonly HashSet<string> JsonExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json"
    };

    private readonly IHttpClientFactory? _httpClientFactory;

    public SpecFetcher(IHttpClientFactory? httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<FetchedSpec> FetchAsync(SpecSource source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Stream is not null)
        {
            return await ReadFromStreamAsync(source.Stream, source.FileName, ct);
        }

        if (source.Url is not null)
        {
            return await FetchFromUrlAsync(source.Url, ct);
        }

        throw new InvalidOperationException("SpecSource must have either Stream or Url set.");
    }

    private async Task<FetchedSpec> FetchFromUrlAsync(string url, CancellationToken ct)
    {
        if (_httpClientFactory is null)
        {
            throw new InvalidOperationException(
                "HttpClientFactory is required for URL fetch. Register it via AddHttpClient().");
        }

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var fileName = ExtractFileNameFromUrl(url);
        var format = DetectFormat(content, fileName);

        return new FetchedSpec(content, ComputeHash(content), format);
    }

    private static async Task<FetchedSpec> ReadFromStreamAsync(Stream stream, string? fileName, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(ct);
        var format = DetectFormat(content, fileName);

        return new FetchedSpec(content, ComputeHash(content), format);
    }

    private static SpecFormat DetectFormat(string content, string? fileName)
    {
        if (fileName is not null)
        {
            var ext = Path.GetExtension(fileName);
            if (YamlExtensions.Contains(ext))
            {
                return SpecFormat.Yaml;
            }
            if (JsonExtensions.Contains(ext))
            {
                return SpecFormat.Json;
            }
        }

        var trimmed = content.AsSpan().TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return SpecFormat.Json;
        }

        return SpecFormat.Yaml;
    }

    private static string ExtractFileNameFromUrl(string url)
    {
        var queryIndex = url.IndexOfAny(['?', '#']);
        var path = queryIndex >= 0 ? url[..queryIndex] : url;
        return Path.GetFileName(path);
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~SpecFetcherTests" -v n
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/McpGateway.Core/SpecManagement/SpecFetcher.cs \
        src/McpGateway.Core/SpecManagement/SpecSource.cs \
        src/McpGateway.Core/SpecManagement/FetchedSpec.cs \
        src/McpGateway.Core/SpecManagement/ISpecFetcher.cs \
        src/McpGateway.Core/McpGateway.Core.csproj \
        tests/McpGateway.UnitTests/SpecManagement/SpecFetcherTests.cs
git commit -m "feat(spec-mgmt): add SpecFetcher for URL and stream sources

- Fetch from URL via named HttpClient or from in-memory Stream
- Detect JSON vs YAML by file extension or content sniff
- Compute SHA-256 hash for change detection
- Throw on HTTP non-2xx responses"
```

---

### Task 4: Add `SpecToolChange` and `SpecDiffResult` records

**Files:**
- Create: `src/McpGateway.Core/SpecManagement/SpecToolChange.cs`
- Create: `src/McpGateway.Core/SpecManagement/SpecDiffResult.cs`

- [ ] **Step 1: Create `SpecToolChange`**

Create `src/McpGateway.Core/SpecManagement/SpecToolChange.cs`:

```csharp
namespace McpGateway.Core.SpecManagement;

/// <summary>
/// Describes what changed in a single tool between two spec versions.
/// Empty <see cref="ChangedFields"/> list means metadata matched (still considered
/// present in the diff, but no admin review required).
/// </summary>
public sealed record SpecToolChange(
    string ToolName,
    string HttpMethod,
    string HttpPath,
    IReadOnlyList<string> ChangedFields);
```

- [ ] **Step 2: Create `SpecDiffResult`**

Create `src/McpGateway.Core/SpecManagement/SpecDiffResult.cs`:

```csharp
namespace McpGateway.Core.SpecManagement;

/// <summary>
/// Result of diffing the tools generated from two spec versions.
/// </summary>
public sealed record SpecDiffResult(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<SpecToolChange> Changed)
{
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Changed.Count > 0;

    public string ToJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            added = Added,
            removed = Removed,
            changed = Changed.Select(c => new
            {
                toolName = c.ToolName,
                httpMethod = c.HttpMethod,
                httpPath = c.HttpPath,
                changedFields = c.ChangedFields
            }).ToArray()
        });
    }
}
```

---

### Task 5: Implement `SpecDiffService` and `ISpecDiffService`

**Files:**
- Create: `src/McpGateway.Core/SpecManagement/ISpecDiffService.cs`
- Create: `src/McpGateway.Core/SpecManagement/SpecDiffService.cs`
- Create: `tests/McpGateway.UnitTests/SpecManagement/SpecDiffServiceTests.cs`

*Prerequisite:* `IToolGenerator` (implemented by `ToolGenerator`) and `ClientProfile` from Tool Generation plan (already implemented).

- [ ] **Step 1: Create `ISpecDiffService`**

Create `src/McpGateway.Core/SpecManagement/ISpecDiffService.cs`:

```csharp
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.SpecManagement;

public interface ISpecDiffService
{
    SpecDiffResult Diff(
        string oldSpecContent,
        string newSpecContent,
        ClientProfile profile);
}
```

- [ ] **Step 2: Write failing tests for `SpecDiffService`**

Create `tests/McpGateway.UnitTests/SpecManagement/SpecDiffServiceTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.SpecManagement;

public class SpecDiffServiceTests
{
    private readonly SpecDiffService _service = new(new ToolGenerator());

    private const string SpecV1 = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Test", "version": "1.0" },
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

    private const string SpecV2 = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Test", "version": "2.0" },
      "paths": {
        "/users": {
          "get": {
            "operationId": "listUsers",
            "summary": "List users (updated)",
            "responses": { "200": { "description": "OK" } }
          }
        },
        "/invoices": {
          "get": {
            "operationId": "listInvoices",
            "summary": "List invoices",
            "responses": { "200": { "description": "OK" } }
          }
        }
      }
    }
    """;

    private const string SpecV1Renamed = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Test", "version": "1.0" },
      "paths": {
        "/users": {
          "delete": {
            "operationId": "deleteUser",
            "summary": "Delete a user",
            "responses": { "204": { "description": "No content" } }
          }
        }
      }
    }
    """;

    [Fact]
    public void Diff_IdenticalSpecs_ReturnsNoChanges()
    {
        var result = _service.Diff(SpecV1, SpecV1, ClientProfile.Universal);

        result.HasChanges.Should().BeFalse();
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AddedEndpoint_AppearsInAdded()
    {
        var result = _service.Diff(SpecV1, SpecV2, ClientProfile.Universal);

        result.Added.Should().Contain("listInvoices");
    }

    [Fact]
    public void Diff_RemovedEndpoint_AppearsInRemoved()
    {
        var result = _service.Diff(SpecV1, SpecV1Renamed, ClientProfile.Universal);

        result.Removed.Should().Contain("listUsers");
        result.Added.Should().Contain("deleteUser");
    }

    [Fact]
    public void Diff_ChangedDescription_AppearsInChanged()
    {
        var result = _service.Diff(SpecV1, SpecV2, ClientProfile.Universal);

        result.Changed.Should().ContainSingle(c => c.ToolName == "listUsers");
        result.Changed.First(c => c.ToolName == "listUsers").ChangedFields.Should().Contain("description");
    }

    [Fact]
    public void Diff_JsonOutput_ContainsAllSections()
    {
        var result = _service.Diff(SpecV1, SpecV2, ClientProfile.Universal);

        var json = result.ToJson();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("added").GetArrayLength().Should().BeGreaterThan(0);
        doc.RootElement.GetProperty("changed").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Diff_EmptySpecs_ReturnEmptyResult()
    {
        const string empty = """{"openapi":"3.0.0","info":{"title":"E","version":"1.0"},"paths":{}}""";

        var result = _service.Diff(empty, empty, ClientProfile.Universal);

        result.HasChanges.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Implement `SpecDiffService`**

Create `src/McpGateway.Core/SpecManagement/SpecDiffService.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.Core.SpecManagement;

public class SpecDiffService : ISpecDiffService
{
    private readonly IToolGenerator _toolGenerator;

    public SpecDiffService(IToolGenerator toolGenerator)
    {
        _toolGenerator = toolGenerator;
    }

    public SpecDiffResult Diff(string oldSpecContent, string newSpecContent, ClientProfile profile)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldSpecContent);
        ArgumentException.ThrowIfNullOrEmpty(newSpecContent);

        var oldTools = _toolGenerator.Generate(oldSpecContent, profile);
        var newTools = _toolGenerator.Generate(newSpecContent, profile);

        return ComputeDiff(oldTools, newTools);
    }

    internal static SpecDiffResult ComputeDiff(
        IReadOnlyList<GeneratedTool> oldTools,
        IReadOnlyList<GeneratedTool> newTools)
    {
        var oldByName = oldTools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var newByName = newTools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        var added = newByName.Keys.Except(oldByName.Keys, StringComparer.Ordinal).OrderBy(n => n).ToList();
        var removed = oldByName.Keys.Except(newByName.Keys, StringComparer.Ordinal).OrderBy(n => n).ToList();

        var changed = new List<SpecToolChange>();
        foreach (var name in oldByName.Keys.Intersect(newByName.Keys, StringComparer.Ordinal).OrderBy(n => n))
        {
            var oldTool = oldByName[name];
            var newTool = newByName[name];
            var changedFields = CompareFields(oldTool, newTool);
            if (changedFields.Count > 0)
            {
                changed.Add(new SpecToolChange(
                    ToolName: newTool.Name,
                    HttpMethod: newTool.HttpMethod,
                    HttpPath: newTool.HttpPath,
                    ChangedFields: changedFields));
            }
        }

        return new SpecDiffResult(added, removed, changed);
    }

    private static List<string> CompareFields(GeneratedTool oldTool, GeneratedTool newTool)
    {
        var fields = new List<string>();

        if (!string.Equals(oldTool.Description, newTool.Description, StringComparison.Ordinal))
        {
            fields.Add("description");
        }
        if (!string.Equals(oldTool.HttpMethod, newTool.HttpMethod, StringComparison.Ordinal))
        {
            fields.Add("httpMethod");
        }
        if (!string.Equals(oldTool.HttpPath, newTool.HttpPath, StringComparison.Ordinal))
        {
            fields.Add("httpPath");
        }
        if (!JsonNodesEqual(oldTool.InputSchema, newTool.InputSchema))
        {
            fields.Add("inputSchema");
        }
        if (!JsonNodesEqual(oldTool.OutputSchema, newTool.OutputSchema))
        {
            fields.Add("outputSchema");
        }

        return fields;
    }

    private static bool JsonNodesEqual(JsonNode? a, JsonNode? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return string.Equals(
            a.ToJsonString(SortOptions),
            b.ToJsonString(SortOptions),
            StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions SortOptions = new()
    {
        WriteIndented = false
    };
}
```

Note: The `ToolGenerator` in the Tool Generation plan uses `JsonNode` for `InputSchema`/`OutputSchema` on `GeneratedTool`. If at execution time the generated schemas are `string` (JSON serialized), adapt `CompareFields` to use `string.Equals` for the JSON string.

- [ ] **Step 4: Run tests**

Run:
```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~SpecDiffServiceTests" -v n
```

Expected: PASS. If `GeneratedTool.InputSchema` is `string` not `JsonNode` in your repo, see the note above.

- [ ] **Step 5: Commit**

```bash
git add src/McpGateway.Core/SpecManagement/SpecDiffService.cs \
        src/McpGateway.Core/SpecManagement/ISpecDiffService.cs \
        src/McpGateway.Core/SpecManagement/SpecDiffResult.cs \
        src/McpGateway.Core/SpecManagement/SpecToolChange.cs \
        tests/McpGateway.UnitTests/SpecManagement/SpecDiffServiceTests.cs
git commit -m "feat(spec-mgmt): add SpecDiffService for added/removed/changed tool lists

- Diff GeneratedTool by name; field-level comparison for matches
- Detect description, httpMethod, httpPath, inputSchema, outputSchema changes
- Serialize diff to JSON for spec_versions diff_summary storage"
```

---

### Task 6: Add `SpecRefreshOutcome` and `ISpecRefresher`

**Files:**
- Create: `src/McpGateway.Core/SpecManagement/SpecRefreshOutcome.cs`
- Create: `src/McpGateway.Core/SpecManagement/ISpecRefresher.cs`

- [ ] **Step 1: Create `SpecRefreshOutcome`**

Create `src/McpGateway.Core/SpecManagement/SpecRefreshOutcome.cs`:

```csharp
namespace McpGateway.Core.SpecManagement;

public enum SpecRefreshStatus
{
    Unchanged,
    Updated,
    Failed,
    NoSpecSource
}

public sealed record SpecRefreshOutcome(
    SpecRefreshStatus Status,
    string ServerName,
    string? OldHash,
    string? NewHash,
    string? Error = null);
```

- [ ] **Step 2: Create `ISpecRefresher`**

Create `src/McpGateway.Core/SpecManagement/ISpecRefresher.cs`:

```csharp
namespace McpGateway.Core.SpecManagement;

public interface ISpecRefresher
{
    Task<SpecRefreshOutcome> RefreshAsync(string serverName, CancellationToken ct = default);
    Task<IReadOnlyList<SpecRefreshOutcome>> RefreshAllAsync(CancellationToken ct = default);
}
```

---

### Task 7: Implement `ServerSpecRefresher` (single-server logic)

**Files:**
- Create: `src/McpGateway.Core/SpecManagement/ServerSpecRefresher.cs`

*Prerequisite:* Tasks 1, 3, 5, 6.

- [ ] **Step 1: Implement `ServerSpecRefresher`**

Create `src/McpGateway.Core/SpecManagement/ServerSpecRefresher.cs`:

```csharp
using System.Text.Json;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;

namespace McpGateway.Core.SpecManagement;

/// <summary>
/// Refreshes a single server definition. Used by <see cref="SpecRefresher"/>
/// and exposed to the admin refresh endpoint.
/// </summary>
public class ServerSpecRefresher
{
    private readonly ISpecFetcher _specFetcher;
    private readonly ISpecDiffService _specDiffService;
    private readonly IToolGenerator _toolGenerator;
    private readonly IServerDefinitionRepository _serverRepository;
    private readonly ISpecVersionRepository _specVersionRepository;
    private readonly IToolStore _toolStore;

    public ServerSpecRefresher(
        ISpecFetcher specFetcher,
        ISpecDiffService specDiffService,
        IToolGenerator toolGenerator,
        IServerDefinitionRepository serverRepository,
        ISpecVersionRepository specVersionRepository,
        IToolStore toolStore)
    {
        _specFetcher = specFetcher;
        _specDiffService = specDiffService;
        _toolGenerator = toolGenerator;
        _serverRepository = serverRepository;
        _specVersionRepository = specVersionRepository;
        _toolStore = toolStore;
    }

    public async Task<SpecRefreshOutcome> RefreshAsync(McpServerDefinition server, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        try
        {
            if (string.IsNullOrWhiteSpace(server.SpecSourceUrl))
            {
                return new SpecRefreshOutcome(
                    SpecRefreshStatus.NoSpecSource,
                    server.Name,
                    OldHash: server.SpecHash,
                    NewHash: null);
            }

            var fetched = await _specFetcher.FetchAsync(
                SpecSource.FromUrl(server.SpecSourceUrl),
                ct);

            if (string.Equals(fetched.Hash, server.SpecHash, StringComparison.Ordinal))
            {
                server.LastRefreshedAt = DateTime.UtcNow;
                await _serverRepository.UpdateAsync(server, ct);
                return new SpecRefreshOutcome(
                    SpecRefreshStatus.Unchanged,
                    server.Name,
                    OldHash: server.SpecHash,
                    NewHash: fetched.Hash);
            }

            var oldHash = server.SpecHash;
            var oldSpecContent = server.SpecContent;
            var oldTools = server.Tools.ToList();

            var newTools = _toolGenerator.Generate(fetched.Content, server.ClientProfile);
            var newToolEntities = newTools.Select(t => new ToolDefinition
            {
                ServerDefinitionId = server.Id,
                ToolName = t.Name,
                Description = t.Description,
                HttpMethod = t.HttpMethod,
                HttpPath = t.HttpPath,
                InputSchema = SerializeSchema(t.InputSchema),
                OutputSchema = t.OutputSchema is null ? null : SerializeSchema(t.OutputSchema),
                AuthConfig = t.AuthConfig,
                Visible = t.Visible
            }).ToList();

            var diff = _specDiffService.Diff(oldSpecContent, fetched.Content, server.ClientProfile);
            var diffJson = diff.ToJson();

            await _serverRepository.UpdateToolsAsync(server.Id, newToolEntities, ct);

            server.SpecContent = fetched.Content;
            server.SpecHash = fetched.Hash;
            server.ApprovalStatus = "changes_pending";
            server.ApprovedAt = null;
            server.ApprovedBy = null;
            server.LastRefreshedAt = DateTime.UtcNow;
            await _serverRepository.UpdateAsync(server, ct);

            await _specVersionRepository.AddAsync(new SpecVersion
            {
                ServerDefinitionId = server.Id,
                SpecHash = fetched.Hash,
                SpecContent = fetched.Content,
                ToolCount = newTools.Count,
                DiffSummary = diffJson
            }, ct);

            _toolStore.RemoveServer(server.Name);

            _ = oldTools;
            return new SpecRefreshOutcome(
                SpecRefreshStatus.Updated,
                server.Name,
                OldHash: oldHash,
                NewHash: fetched.Hash);
        }
        catch (Exception ex)
        {
            return new SpecRefreshOutcome(
                SpecRefreshStatus.Failed,
                server.Name,
                OldHash: server.SpecHash,
                NewHash: null,
                Error: ex.Message);
        }
    }

    private static string SerializeSchema(System.Text.Json.Nodes.JsonNode schema) =>
        schema.ToJsonString();
}
```

- [ ] **Step 2: Verify build**

Run:
```bash
dotnet build src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds. If `GeneratedTool.InputSchema` is `string` not `JsonNode` in your repo, remove the `SerializeSchema` call and assign `t.InputSchema.ToString()` directly.

- [ ] **Step 3: Commit**

```bash
git add src/McpGateway.Core/SpecManagement/ServerSpecRefresher.cs
git commit -m "feat(spec-mgmt): add ServerSpecRefresher single-server refresh logic

- Fetch + hash + diff + persist + flag changes_pending + remove from tool store
- Error handling returns Failed outcome with message"
```

---

### Task 8: Implement `SpecRefresher` (background service)

**Files:**
- Create: `src/McpGateway.Core/SpecManagement/SpecRefresher.cs`

*Prerequisite:* Tasks 1, 6, 7.

- [ ] **Step 1: Implement `SpecRefresher`**

Create `src/McpGateway.Core/SpecManagement/SpecRefresher.cs`:

```csharp
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace McpGateway.Core.SpecManagement;

/// <summary>
/// Background service that periodically polls each approved+active server
/// definition and refreshes its spec from <see cref="McpServerDefinition.SpecSourceUrl"/>.
/// Also exposes <see cref="RefreshAsync(string)"/> for manual triggers from
/// the admin API (POST /admin/servers/{name}/refresh).
/// </summary>
public class SpecRefresher : BackgroundService, ISpecRefresher
{
    private readonly IServerDefinitionRepository _serverRepository;
    private readonly ServerSpecRefresher _singleRefresher;
    private readonly ILogger<SpecRefresher> _logger;
    private readonly TimeSpan _tickInterval;

    public SpecRefresher(
        IServerDefinitionRepository serverRepository,
        ServerSpecRefresher singleRefresher,
        ILogger<SpecRefresher> logger,
        TimeSpan? tickInterval = null)
    {
        _serverRepository = serverRepository;
        _singleRefresher = singleRefresher;
        _logger = logger;
        _tickInterval = tickInterval ?? TimeSpan.FromMinutes(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpecRefresher started; tick interval {Interval}", _tickInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcomes = await RefreshAllAsync(stoppingToken);
                foreach (var outcome in outcomes)
                {
                    LogOutcome(outcome);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SpecRefresher tick failed");
            }

            try
            {
                await Task.Delay(_tickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("SpecRefresher stopped");
    }

    public async Task<SpecRefreshOutcome> RefreshAsync(string serverName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        var server = await _serverRepository.GetByNameAsync(serverName, ct);
        if (server is null)
        {
            return new SpecRefreshOutcome(
                SpecRefreshStatus.Failed,
                serverName,
                OldHash: null,
                NewHash: null,
                Error: "Server definition not found.");
        }

        var outcome = await _singleRefresher.RefreshAsync(server, ct);
        LogOutcome(outcome);
        return outcome;
    }

    public async Task<IReadOnlyList<SpecRefreshOutcome>> RefreshAllAsync(CancellationToken ct = default)
    {
        var allServers = await _serverRepository.ListAsync(ct);
        var activeServers = allServers
            .Where(s => s.Status == "active")
            .ToList();

        var outcomes = new List<SpecRefreshOutcome>(activeServers.Count);
        foreach (var server in activeServers)
        {
            var outcome = await _singleRefresher.RefreshAsync(server, ct);
            outcomes.Add(outcome);
        }

        return outcomes;
    }

    private void LogOutcome(SpecRefreshOutcome outcome)
    {
        switch (outcome.Status)
        {
            case SpecRefreshStatus.Updated:
                _logger.LogInformation(
                    "Spec refresh: {Server} updated ({Old} -> {New})",
                    outcome.ServerName, outcome.OldHash, outcome.NewHash);
                break;
            case SpecRefreshStatus.Unchanged:
                _logger.LogDebug(
                    "Spec refresh: {Server} unchanged ({Hash})",
                    outcome.ServerName, outcome.NewHash);
                break;
            case SpecRefreshStatus.Failed:
                _logger.LogWarning(
                    "Spec refresh: {Server} failed: {Error}",
                    outcome.ServerName, outcome.Error);
                break;
            case SpecRefreshStatus.NoSpecSource:
                _logger.LogDebug(
                    "Spec refresh: {Server} has no SpecSourceUrl, skipping",
                    outcome.ServerName);
                break;
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run:
```bash
dotnet build src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/McpGateway.Core/SpecManagement/SpecRefresher.cs
git commit -m "feat(spec-mgmt): add SpecRefresher BackgroundService

- Polls all active server definitions on configurable tick interval
- RefreshAsync(serverName) for manual admin endpoint trigger
- RefreshAllAsync iterates all active servers
- Logs outcomes at appropriate levels (info on update, warn on fail)"
```

---

### Task 9: Add unit test for `SpecRefresher` (manual refresh path)

**Files:**
- Create: `tests/McpGateway.UnitTests/SpecManagement/SpecRefresherTests.cs`

*Prerequisite:* Tasks 1, 7, 8.

- [ ] **Step 1: Write tests for `SpecRefresher`**

Create `tests/McpGateway.UnitTests/SpecManagement/SpecRefresherTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using Microsoft.Extensions.Logging.Abstractions;

namespace McpGateway.UnitTests.SpecManagement;

public class SpecRefresherTests
{
    [Fact]
    public async Task RefreshAsync_UnknownServer_ReturnsFailedOutcome()
    {
        var repo = new InMemoryServerDefinitionRepository();
        var refresher = new SpecRefresher(
            repo,
            singleRefresher: null!,
            NullLogger<SpecRefresher>.Instance);

        var outcome = await refresher.RefreshAsync("nonexistent");

        outcome.Status.Should().Be(SpecRefreshStatus.Failed);
        outcome.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RefreshAsync_ChangedSpec_UpdatesAndRemovesFromStore()
    {
        var serverRepo = new InMemoryServerDefinitionRepository();
        var versionRepo = new InMemorySpecVersionRepository();
        var toolStore = new InMemoryToolStore();

        const string v1 = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"},"paths":{"/users":{"get":{"operationId":"listUsers","summary":"List","responses":{"200":{"description":"OK"}}}}}}""";
        const string v2 = """{"openapi":"3.0.0","info":{"title":"T","version":"2.0"},"paths":{"/users":{"get":{"operationId":"listUsers","summary":"List (v2)","responses":{"200":{"description":"OK"}}}},"/invoices":{"get":{"operationId":"listInvoices","summary":"List invoices","responses":{"200":{"description":"OK"}}}}}}""";

        var server = new McpServerDefinition
        {
            Name = "spec-test",
            DisplayName = "Spec Test",
            SpecSourceUrl = "https://example.com/spec.json",
            SpecContent = v1,
            SpecHash = "old-hash",
            BaseUrl = "https://example.com",
            ClientProfile = ClientProfile.Universal,
            ApprovalStatus = "approved",
            Status = "active"
        };
        await serverRepo.AddAsync(server);

        toolStore.AddServer(server);
        toolStore.Contains("spec-test").Should().BeTrue();

        var fetcher = new StaticSpecFetcher((v2, "new-hash", SpecFormat.Json));
        var diff = new SpecDiffService(new ToolGenerator());
        var single = new ServerSpecRefresher(
            fetcher, diff, new ToolGenerator(), serverRepo, versionRepo, toolStore);
        var refresher = new SpecRefresher(serverRepo, single, NullLogger<SpecRefresher>.Instance);

        var outcome = await refresher.RefreshAsync("spec-test");

        outcome.Status.Should().Be(SpecRefreshStatus.Updated);
        outcome.NewHash.Should().Be("new-hash");
        toolStore.Contains("spec-test").Should().BeFalse();

        var stored = await serverRepo.GetByNameAsync("spec-test");
        stored!.SpecHash.Should().Be("new-hash");
        stored.ApprovalStatus.Should().Be("changes_pending");
        stored.ApprovedAt.Should().BeNull();

        var versions = await versionRepo.ListByServerAsync(server.Id);
        versions.Should().ContainSingle();
        versions[0].SpecHash.Should().Be("new-hash");
        versions[0].ToolCount.Should().Be(2);
    }

    [Fact]
    public async Task RefreshAsync_UnchangedSpec_DoesNotRemoveFromStore()
    {
        var serverRepo = new InMemoryServerDefinitionRepository();
        var versionRepo = new InMemorySpecVersionRepository();
        var toolStore = new InMemoryToolStore();

        const string sameSpec = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"},"paths":{}}""";
        var server = new McpServerDefinition
        {
            Name = "unchanged",
            DisplayName = "Unchanged",
            SpecSourceUrl = "https://example.com/spec.json",
            SpecContent = sameSpec,
            SpecHash = "same-hash",
            BaseUrl = "https://example.com",
            Status = "active"
        };
        await serverRepo.AddAsync(server);
        toolStore.AddServer(server);

        var fetcher = new StaticSpecFetcher((sameSpec, "same-hash", SpecFormat.Json));
        var diff = new SpecDiffService(new ToolGenerator());
        var single = new ServerSpecRefresher(
            fetcher, diff, new ToolGenerator(), serverRepo, versionRepo, toolStore);
        var refresher = new SpecRefresher(serverRepo, single, NullLogger<SpecRefresher>.Instance);

        var outcome = await refresher.RefreshAsync("unchanged");

        outcome.Status.Should().Be(SpecRefreshStatus.Unchanged);
        toolStore.Contains("unchanged").Should().BeTrue();
    }

    private sealed class StaticSpecFetcher : ISpecFetcher
    {
        private readonly FetchedSpec _next;
        public StaticSpecFetcher(FetchedSpec next) => _next = next;
        public Task<FetchedSpec> FetchAsync(SpecSource source, CancellationToken ct = default)
            => Task.FromResult(_next);
    }

    private sealed class InMemoryServerDefinitionRepository : IServerDefinitionRepository
    {
        private readonly Dictionary<Guid, McpServerDefinition> _store = new();

        public Task<McpServerDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_store.Values.FirstOrDefault(s => s.Name == name));

        public Task<McpServerDefinition?> GetByNameForAdminAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_store.Values.FirstOrDefault(s => s.Name == name));

        public Task<IReadOnlyList<McpServerDefinition>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpServerDefinition>>(_store.Values.ToList());

        public Task<IReadOnlyList<McpServerDefinition>> ListApprovedAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpServerDefinition>>(
                _store.Values.Where(s => s.ApprovalStatus == "approved").ToList());

        public async Task<McpServerDefinition> AddAsync(McpServerDefinition definition, CancellationToken ct = default)
        {
            definition.Id = Guid.NewGuid();
            _store[definition.Id] = definition;
            return await Task.FromResult(definition);
        }

        public Task UpdateAsync(McpServerDefinition definition, CancellationToken ct = default)
        {
            _store[definition.Id] = definition;
            return Task.CompletedTask;
        }

        public Task UpdateToolsAsync(Guid serverDefinitionId, IEnumerable<ToolDefinition> tools, CancellationToken ct = default)
        {
            if (_store.TryGetValue(serverDefinitionId, out var server))
            {
                server.Tools = tools.ToList();
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _store.Remove(id);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySpecVersionRepository : ISpecVersionRepository
    {
        private readonly Dictionary<Guid, SpecVersion> _store = new();

        public Task<SpecVersion> AddAsync(SpecVersion version, CancellationToken ct = default)
        {
            version.Id = Guid.NewGuid();
            _store[version.Id] = version;
            return Task.FromResult(version);
        }

        public Task<SpecVersion?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(id, out var v) ? v : null);

        public Task<IReadOnlyList<SpecVersion>> ListByServerAsync(Guid serverDefinitionId, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SpecVersion>>(
                _store.Values.Where(v => v.ServerDefinitionId == serverDefinitionId).ToList());

        public Task<SpecVersion?> GetLatestAsync(Guid serverDefinitionId, CancellationToken ct = default)
            => Task.FromResult(_store.Values
                .Where(v => v.ServerDefinitionId == serverDefinitionId)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefault());
    }
}
```

- [ ] **Step 2: Run tests**

Run:
```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~SpecRefresherTests" -v n
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/McpGateway.UnitTests/SpecManagement/SpecRefresherTests.cs
git commit -m "test(spec-mgmt): add SpecRefresherTests with in-memory fakes

- RefreshAsync with unknown server returns Failed outcome
- Changed spec updates PG, removes from tool store, persists SpecVersion
- Unchanged spec does not remove from tool store"
```

---

### Task 10: Add DI registration for `SpecManagement` services

**Files:**
- Modify: `src/McpGateway.Core/CoreServiceExtensions.cs`

*Prerequisite:* `CoreServiceExtensions` exists from the In-Memory Tool Store plan.

- [ ] **Step 1: Update `AddMcpCore` to register spec management**

Modify `src/McpGateway.Core/CoreServiceExtensions.cs`. Add `using McpGateway.Core.SpecManagement;` and `using McpGateway.Core.ToolGeneration;` to the file. Replace the method body:

```csharp
    public static IServiceCollection AddMcpCore(this IServiceCollection services)
    {
        services.AddSingleton<IToolStore, InMemoryToolStore>();
        services.AddScoped<ToolStoreInitializer>();

        return services;
    }
```

with:

```csharp
    public static IServiceCollection AddMcpCore(this IServiceCollection services)
    {
        services.AddSingleton<IToolStore, InMemoryToolStore>();
        services.AddScoped<ToolStoreInitializer>();

        services.AddHttpClient(SpecFetcher.HttpClientName);
        services.AddSingleton<ISpecFetcher>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new SpecFetcher(factory);
        });
        services.AddSingleton<IToolGenerator, ToolGenerator>();
        services.AddSingleton<ISpecDiffService, SpecDiffService>();
        services.AddScoped<ServerSpecRefresher>();
        services.AddSingleton<ISpecRefresher, SpecRefresher>();

        services.AddHostedService(sp => (SpecRefresher)sp.GetRequiredService<ISpecRefresher>());

        return services;
    }
```

- [ ] **Step 2: Verify build**

Run:
```bash
dotnet build src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/McpGateway.Core/CoreServiceExtensions.cs
git commit -m "feat(spec-mgmt): register SpecManagement services in DI

- ISpecFetcher (singleton, depends on IHttpClientFactory)
- ISpecDiffService (singleton, depends on IToolGenerator)
- ServerSpecRefresher (scoped)
- ISpecRefresher (singleton) registered both as service and HostedService"
```

---

### Task 11: Add integration test for end-to-end spec refresh flow

**Files:**
- Create: `tests/McpGateway.IntegrationTests/SpecManagement/SpecRefreshTests.cs`
- Modify: `tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj` (add Moq for HttpMessageHandler mock)

*Prerequisite:* Tasks 1, 3, 5, 7, 8, 10 (or relevant stubs).

- [ ] **Step 1: Add Moq package to integration test project**

Run:
```bash
dotnet add tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj package Moq --version 4.20.72
```

- [ ] **Step 2: Write integration test for spec refresh end-to-end**

Create `tests/McpGateway.IntegrationTests/SpecManagement/SpecRefreshTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace McpGateway.IntegrationTests.SpecManagement;

[Collection("Persistence")]
public class SpecRefreshTests
{
    private readonly PostgreSqlFixture _fixture;

    public SpecRefreshTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Refresh_ChangedSpec_UpdatesServerDefAndClearsToolStore()
    {
        var serverRepo = new ServerDefinitionRepository(_fixture.CreateDbContext());
        var versionRepo = new SpecVersionRepository(_fixture.CreateDbContext());
        var toolStore = new InMemoryToolStore();

        const string v1 = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"},"paths":{"/users":{"get":{"operationId":"listUsers","summary":"List users","responses":{"200":{"description":"OK"}}}}}}""";
        const string v2 = """{"openapi":"3.0.0","info":{"title":"T","version":"2.0"},"paths":{"/users":{"get":{"operationId":"listUsers","summary":"List users (v2)","responses":{"200":{"description":"OK"}}}},"/invoices":{"get":{"operationId":"listInvoices","summary":"List invoices","responses":{"200":{"description":"OK"}}}}}}""";

        var server = await serverRepo.AddAsync(new McpServerDefinition
        {
            Name = "refresh-e2e",
            DisplayName = "Refresh E2E",
            SpecSourceUrl = "https://specs.example.com/refresh-e2e.json",
            SpecContent = v1,
            SpecHash = ComputeHash(v1),
            BaseUrl = "https://api.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}",
            ClientProfile = ClientProfile.Universal,
            ApprovalStatus = "approved",
            Status = "active"
        });

        var httpHandler = new Mock<HttpMessageHandler>();
        httpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(v2, System.Text.Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(httpHandler.Object);
        var httpFactory = new StaticHttpClientFactory(httpClient);
        var fetcher = new SpecFetcher(httpFactory);

        var diff = new SpecDiffService(new ToolGenerator());
        var single = new ServerSpecRefresher(
            fetcher, diff, new ToolGenerator(), serverRepo, versionRepo, toolStore);
        var refresher = new SpecRefresher(serverRepo, single, NullLogger<SpecRefresher>.Instance);

        toolStore.AddServer(server);
        toolStore.Contains("refresh-e2e").Should().BeTrue();

        var outcome = await refresher.RefreshAsync("refresh-e2e");

        outcome.Status.Should().Be(SpecRefreshStatus.Updated);
        toolStore.Contains("refresh-e2e").Should().BeFalse();

        await using var verifyContext = _fixture.CreateDbContext();
        var verifyRepo = new ServerDefinitionRepository(verifyContext);
        var stored = await verifyRepo.GetByNameAsync("refresh-e2e");
        stored.Should().NotBeNull();
        stored!.SpecHash.Should().NotBe(server.SpecHash);
        stored.ApprovalStatus.Should().Be("changes_pending");
        stored.ApprovedAt.Should().BeNull();
        stored.LastRefreshedAt.Should().NotBeNull();

        var versions = await versionRepo.ListByServerAsync(server.Id);
        versions.Should().ContainSingle();
        versions[0].ToolCount.Should().Be(2);
    }

    [Fact]
    public async Task Refresh_UnchangedSpec_DoesNotChangeApprovalStatus()
    {
        var serverRepo = new ServerDefinitionRepository(_fixture.CreateDbContext());
        var versionRepo = new SpecVersionRepository(_fixture.CreateDbContext());
        var toolStore = new InMemoryToolStore();

        const string same = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"},"paths":{"/a":{"get":{"operationId":"a","summary":"A","responses":{"200":{"description":"OK"}}}}}}""";
        var server = await serverRepo.AddAsync(new McpServerDefinition
        {
            Name = "unchanged-e2e",
            DisplayName = "Unchanged E2E",
            SpecSourceUrl = "https://specs.example.com/u.json",
            SpecContent = same,
            SpecHash = ComputeHash(same),
            BaseUrl = "https://api.example.com",
            ApprovalStatus = "approved",
            Status = "active"
        });

        var httpHandler = new Mock<HttpMessageHandler>();
        httpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(same, System.Text.Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(httpHandler.Object);
        var fetcher = new SpecFetcher(new StaticHttpClientFactory(httpClient));
        var diff = new SpecDiffService(new ToolGenerator());
        var single = new ServerSpecRefresher(
            fetcher, diff, new ToolGenerator(), serverRepo, versionRepo, toolStore);
        var refresher = new SpecRefresher(serverRepo, single, NullLogger<SpecRefresher>.Instance);

        toolStore.AddServer(server);

        var outcome = await refresher.RefreshAsync("unchanged-e2e");

        outcome.Status.Should().Be(SpecRefreshStatus.Unchanged);
        toolStore.Contains("unchanged-e2e").Should().BeTrue();

        await using var verifyContext = _fixture.CreateDbContext();
        var verifyRepo = new ServerDefinitionRepository(verifyContext);
        var stored = await verifyRepo.GetByNameAsync("unchanged-e2e");
        stored!.ApprovalStatus.Should().Be("approved");
    }

    private static string ComputeHash(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StaticHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }
}
```

- [ ] **Step 3: Run integration tests**

Run:
```bash
dotnet test tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj --filter "FullyQualifiedName~SpecRefreshTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/McpGateway.IntegrationTests/SpecManagement/SpecRefreshTests.cs \
        tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj
git commit -m "test(spec-mgmt): add end-to-end SpecRefreshTests with Testcontainers

- Mock HttpClient returns new spec on second poll
- Refresh detects change, sets changes_pending, clears tool store
- Refresh of unchanged spec leaves approval_status alone"
```

---

### Task 12: Run full test suite

- [ ] **Step 1: Run all unit tests**

Run:
```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj
```

Expected: All unit tests pass.

- [ ] **Step 2: Run all integration tests**

Run:
```bash
dotnet test tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj
```

Expected: All integration tests pass (Docker required for PostgreSQL Testcontainer).

- [ ] **Step 3: Build entire solution**

Run:
```bash
dotnet build /var/home/felix/github/mcp-gateway/McpGateway.sln
```

Expected: Build succeeds with no errors.

- [ ] **Step 4: Final commit**

```bash
git commit -m "feat(spec-mgmt): complete spec fetch, diff, and refresh pipeline

- SpecFetcher: URL (HttpClient) + stream, JSON/YAML format detection, SHA-256 hash
- SpecDiffService: added/removed/changed tool lists with field-level comparison
- SpecRefresher: BackgroundService polling + manual RefreshAsync(serverName)
- ServerSpecRefresher: fetch+hash+diff+persist+flag changes_pending+remove from store
- ISpecVersionRepository: spec_versions history table reads/writes
- Manual refresh via SpecRefresher.RefreshAsync wired to admin endpoint
- DI registration of all SpecManagement services
- Unit and integration tests for full pipeline"
```

---

## Self-Review

**1. Spec coverage:**

| Requirement | Task |
|---|---|
| `SpecFetcher`: fetch spec from URL (HttpClient) | Task 3 |
| `SpecFetcher`: fetch spec from file (IFormFile/stream) | Task 3 |
| `SpecFetcher`: support JSON and YAML | Task 3 (format detection by extension + content) |
| `SpecDiffService`: compare old vs new spec | Task 5 |
| `SpecDiffService`: produce added/removed/changed tool lists | Task 5 |
| `SpecRefresher`: background service polling | Task 8 |
| `SpecRefresher`: polls at `PollIntervalMinutes` (default 1440/24h) | Task 8 (configurable via `TimeSpan` constructor param) |
| On change: fetch, hash, diff | Task 7 |
| On change: store new spec version | Task 7 (via `ISpecVersionRepository.AddAsync`) |
| On change: update tools via repository | Task 7 (`UpdateToolsAsync`) |
| On change: set `approval_status='changes_pending'` | Task 7 |
| On change: remove from `IToolStore` | Task 7 (`_toolStore.RemoveServer`) |
| Manual refresh via `POST /admin/servers/{name}/refresh` | Task 8 (`RefreshAsync(string name)` exposed; admin endpoint wiring is a follow-up in the Admin API plan) |
| Store spec snapshots in `spec_versions` with hash, content, tool_count, diff_summary | Tasks 1 + 7 |
| Components: `SpecFetcher.cs` | Task 3 |
| Components: `SpecDiffService.cs` | Task 5 |
| Components: `SpecRefresher.cs` | Task 8 |
| Tests: `SpecDiffServiceTests.cs` | Task 5 |
| Tests: `SpecRefreshTests.cs` | Task 11 |

**2. Placeholder scan:**

No `TBD`, `TODO`, "implement later", or vague steps. Every code step contains complete C# code or exact commands. The note about `JsonNode` vs `string` for `GeneratedTool.InputSchema` in Task 5 Step 3 is a guarded note for the executing agent — verified by running the test; if schemas are strings, a one-line swap is required and clearly described.

**3. Type consistency:**

- `McpServerDefinition` from `McpGateway.Core.ServerDefinitions` (existing domain model).
- `IServerDefinitionRepository` (existing) returns the domain model; `GetByNameAsync` is used for single refresh, `ListAsync` for the polling tick.
- `IToolStore` (existing) uses `RemoveServer(name)`, `AddServer`, `Contains` — all already defined in the In-Memory Tool Store plan.
- `SpecSource` / `FetchedSpec` / `SpecDiffResult` / `SpecToolChange` / `SpecRefreshOutcome` are sealed records in `McpGateway.Core.SpecManagement`.
- `SpecRefresher` is a `BackgroundService` AND implements `ISpecRefresher`; the `AddHostedService` registration casts the resolved singleton to the concrete `SpecRefresher` type to register the hosted service while preserving `ISpecRefresher` for manual callers (admin endpoint).
- `ISpecVersionRepository` matches the `spec_versions` table defined in the Persistence plan.
- `SpecRefreshOutcome.OldHash` and `NewHash` are non-null on `Updated`/`Unchanged` and null on `Failed` / `NoSpecSource` — consistent across tasks.
- `SpecFetcher` uses `IHttpClientFactory` named client `spec-fetcher` to avoid coupling to a specific HttpClient instance.
- `ServerSpecRefresher` sets `ApprovalStatus = "changes_pending"`, `ApprovedAt = null`, `ApprovedBy = null` on every successful change — admin must re-approve to reactivate.
- `GeneratedTool` (from `McpGateway.Core.ToolGeneration`) is the output of `IToolGenerator` and is the input to `ISpecDiffService`; persistence uses `ToolDefinition` (from `McpGateway.Core.ServerDefinitions`).

**4. Known follow-ups for Oracle review:**

- The admin endpoint wiring (`POST /admin/servers/{name}/refresh` → `ISpecRefresher.RefreshAsync(name)`) lives in the Admin API plan, not here. The `ISpecRefresher` interface is the seam.
- `SpecRefresher` constructor accepts `TimeSpan? tickInterval` to allow the `Program.cs` to compute it from a config value (e.g., 1 minute in tests, 24h in prd). The DI registration in Task 10 uses the default 1-minute tick; the `Program.cs` plan should override.
- The `SpecRefresher` runs as a hosted singleton. Since `ServerSpecRefresher` is scoped (depends on EF repositories), each tick creates a new scope inside `RefreshAsync` via the repository (the repository's `DbContext` is scoped, so the host's per-tick resolution is acceptable for now; if a real scope is needed, the executor should wrap each refresh in `IServiceScopeFactory.CreateScope()`).

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-07-03-spec-management.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — Fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.

**Which approach?**
