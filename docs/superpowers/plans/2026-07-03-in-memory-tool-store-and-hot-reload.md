# In-Memory Tool Store & Hot Reload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the runtime tool cache: a thread-safe, in-memory store that holds only approved `McpServerDefinition` objects, plus a startup initializer that loads them from PostgreSQL and a hot-reload API for adding/updating/removing servers without restarting the gateway.

**Architecture:** `IToolStore` is the abstraction. `InMemoryToolStore` implements it with `ConcurrentDictionary<string, McpServerDefinition>` keyed by server name. `ToolStoreInitializer` runs at startup: query `IServerDefinitionRepository.ListApprovedAsync()` for approved servers, map `McpServerDefinitionEntity` rows to the shared `McpServerDefinition` domain model via the persistence mapper, and populate the store. Admin approval, spec refresh, and server deletion call `IToolStore.AddServer`, `UpdateServer`, or `RemoveServer` to mutate the runtime view immediately (ADR-0003).

**Tech Stack:** .NET 10, `System.Collections.Concurrent`, EF Core (already referenced via Persistence), xUnit, FluentAssertions, Testcontainers for integration tests.

---

## File Structure

```
src/McpGateway.Core/
├── ToolStore/
│   ├── IToolStore.cs
│   ├── InMemoryToolStore.cs
│   └── ToolStoreInitializer.cs
└── Mapping/
    └── PersistenceToRuntimeMapper.cs

tests/McpGateway.UnitTests/
└── ToolStore/
    └── InMemoryToolStoreTests.cs

tests/McpGateway.IntegrationTests/
└── ToolStore/
    └── ToolStoreInitializerTests.cs
```

---

### Task 1: Verify shared `McpServerDefinition` model

**Files:**
- Assumed existing: `src/McpGateway.Core/ServerDefinitions/McpServerDefinition.cs`
- Create: `tests/McpGateway.UnitTests/ToolStore/InMemoryToolStoreTests.cs`

*Prerequisite:* The Persistence & Database plan must already be implemented so `McpServerDefinition` exists in `McpGateway.Core.ServerDefinitions`.

- [ ] **Step 1: Write failing test**

Create `tests/McpGateway.UnitTests/ToolStore/InMemoryToolStoreTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;

namespace McpGateway.UnitTests.ToolStore;

public class InMemoryToolStoreTests
{
    [Fact]
    public void AddServer_ThenGet_ReturnsServer()
    {
        var store = new InMemoryToolStore();
        var server = new McpServerDefinition
        {
            Name = "invoice-api",
            DisplayName = "Invoice API",
            BaseUrl = "https://invoice.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}"
        };

        store.AddServer(server);
        var retrieved = store.GetServer("invoice-api");

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("invoice-api");
    }
}
```

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~InMemoryToolStoreTests" -v n
```

Expected: FAIL with "type or namespace InMemoryToolStore could not be found".

- [ ] **Step 2: Verify build**

Run:

```bash
dotnet build src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds (shared model already exists from persistence plan).

---

### Task 2: Implement `IToolStore` and `InMemoryToolStore`

**Files:**
- Create: `src/McpGateway.Core/ToolStore/IToolStore.cs`
- Create: `src/McpGateway.Core/ToolStore/InMemoryToolStore.cs`

- [ ] **Step 1: Define `IToolStore`**

Create `src/McpGateway.Core/ToolStore/IToolStore.cs`:

```csharp
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.ToolStore;

public interface IToolStore
{
    McpServerDefinition? GetServer(string name);
    IReadOnlyCollection<McpServerDefinition> GetAllServers();
    void AddServer(McpServerDefinition definition);
    void UpdateServer(McpServerDefinition definition);
    bool RemoveServer(string name);
    bool Contains(string name);
}
```

- [ ] **Step 2: Implement `InMemoryToolStore`**

Create `src/McpGateway.Core/ToolStore/InMemoryToolStore.cs`:

```csharp
using System.Collections.Concurrent;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.ToolStore;

public class InMemoryToolStore : IToolStore
{
    private readonly ConcurrentDictionary<string, McpServerDefinition> _servers = new();

    public McpServerDefinition? GetServer(string name)
        => _servers.TryGetValue(name, out var definition) ? definition : null;

    public IReadOnlyCollection<McpServerDefinition> GetAllServers()
        => _servers.Values.ToList().AsReadOnly();

    public void AddServer(McpServerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);

        _servers[definition.Name] = definition;
    }

    public void UpdateServer(McpServerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);

        _servers[definition.Name] = definition;
    }

    public bool RemoveServer(string name)
        => _servers.TryRemove(name, out _);

    public bool Contains(string name)
        => _servers.ContainsKey(name);
}
```

- [ ] **Step 3: Complete `InMemoryToolStoreTests`**

Replace the placeholder test in `tests/McpGateway.UnitTests/ToolStore/InMemoryToolStoreTests.cs` with full tests:

```csharp
using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;

namespace McpGateway.UnitTests.ToolStore;

public class InMemoryToolStoreTests
{
    private readonly InMemoryToolStore _store = new();

    private static McpServerDefinition CreateServer(string name) => new()
    {
        Name = name,
        DisplayName = name,
        BaseUrl = $"https://{name}.example.com",
        SpecHash = "hash",
        AuthStrategy = "obo",
        AuthConfig = "{}"
    };

    [Fact]
    public void AddServer_ThenGet_ReturnsServer()
    {
        var server = CreateServer("invoice-api");

        _store.AddServer(server);
        var retrieved = _store.GetServer("invoice-api");

        retrieved.Should().BeSameAs(server);
    }

    [Fact]
    public void GetServer_Unknown_ReturnsNull()
    {
        var retrieved = _store.GetServer("unknown");
        retrieved.Should().BeNull();
    }

    [Fact]
    public void UpdateServer_ReplacesServer()
    {
        var first = CreateServer("api");
        var second = CreateServer("api");
        second.BaseUrl = "https://updated.example.com";

        _store.AddServer(first);
        _store.UpdateServer(second);

        _store.GetServer("api")!.BaseUrl.Should().Be("https://updated.example.com");
    }

    [Fact]
    public void RemoveServer_Existing_ReturnsTrue()
    {
        _store.AddServer(CreateServer("api"));
        _store.RemoveServer("api").Should().BeTrue();
        _store.Contains("api").Should().BeFalse();
    }

    [Fact]
    public void RemoveServer_Unknown_ReturnsFalse()
    {
        _store.RemoveServer("unknown").Should().BeFalse();
    }

    [Fact]
    public void GetAllServers_ReturnsAll()
    {
        _store.AddServer(CreateServer("a"));
        _store.AddServer(CreateServer("b"));

        _store.GetAllServers().Should().HaveCount(2);
    }

    [Fact]
    public void AddServer_Null_Throws()
    {
        Action act = () => _store.AddServer(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddServer_EmptyName_Throws()
    {
        Action act = () => _store.AddServer(new McpServerDefinition { Name = "" });
        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 4: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~InMemoryToolStoreTests" -v n
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/McpGateway.Core/ToolStore tests/McpGateway.UnitTests/ToolStore
git commit -m "feat(tool-store): add InMemoryToolStore

- Thread-safe ConcurrentDictionary-backed store
- AddServer, UpdateServer, RemoveServer, GetServer, GetAllServers, Contains"
```

---

### Task 3: Remove separate persistence-to-runtime mapper

**Files:**
- None (delete planned `src/McpGateway.Core/Mapping/PersistenceToRuntimeMapper.cs` and `tests/McpGateway.UnitTests/Mapping/`)

- [ ] **Step 1: Rely on repository mapping**

The Persistence & Database plan's `IServerDefinitionRepository.ListApprovedAsync()` already returns `McpServerDefinition` domain models mapped from EF entities. No additional `PersistenceToRuntimeMapper` is needed.

Remove these planned files from the task list:
- `src/McpGateway.Core/Mapping/PersistenceToRuntimeMapper.cs`
- `tests/McpGateway.UnitTests/Mapping/PersistenceToRuntimeMapperTests.cs`

- [ ] **Step 2: Update file structure**

The `src/McpGateway.Core/Mapping/` directory is no longer required for this plan.

---

### Task 4: Implement `ToolStoreInitializer`

**Files:**
- Create: `src/McpGateway.Core/ToolStore/ToolStoreInitializer.cs`
- Create: `tests/McpGateway.IntegrationTests/ToolStore/ToolStoreInitializerTests.cs`

- [ ] **Step 1: Write failing test**

Create `tests/McpGateway.IntegrationTests/ToolStore/ToolStoreInitializerTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using McpGateway.Persistence;
using McpGateway.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace McpGateway.IntegrationTests.ToolStore;

[Collection("Persistence")]
public class ToolStoreInitializerTests
{
    private readonly PostgreSqlFixture _fixture;

    public ToolStoreInitializerTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InitializeAsync_LoadsOnlyApprovedServers()
    {
        await using var context = _fixture.CreateDbContext();
        var repo = new ServerDefinitionRepository(context);

        var approved = new McpServerDefinition
        {
            Name = "approved-api",
            DisplayName = "Approved",
            SpecContent = "{}",
            SpecHash = "hash1",
            BaseUrl = "https://approved.example.com",
            ApprovalStatus = "approved"
        };
        var pending = new McpServerDefinition
        {
            Name = "pending-api",
            DisplayName = "Pending",
            SpecContent = "{}",
            SpecHash = "hash2",
            BaseUrl = "https://pending.example.com",
            ApprovalStatus = "pending"
        };

        await repo.AddAsync(approved);
        await repo.AddAsync(pending);

        var store = new InMemoryToolStore();
        var initializer = new ToolStoreInitializer(store, repo);
        await initializer.InitializeAsync();

        store.GetServer("approved-api").Should().NotBeNull();
        store.GetServer("pending-api").Should().BeNull();
    }
}
```

Run tests, expect FAIL.

- [ ] **Step 2: Implement `ToolStoreInitializer`**

Create `src/McpGateway.Core/ToolStore/ToolStoreInitializer.cs`:

```csharp
using McpGateway.Core.Repositories;

namespace McpGateway.Core.ToolStore;

public class ToolStoreInitializer
{
    private readonly IToolStore _toolStore;
    private readonly IServerDefinitionRepository _repository;

    public ToolStoreInitializer(IToolStore toolStore, IServerDefinitionRepository repository)
    {
        _toolStore = toolStore;
        _repository = repository;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var approvedServers = await _repository.ListApprovedAsync(ct);

        foreach (var server in approvedServers)
        {
            _toolStore.AddServer(server);
        }
    }
}
```

- [ ] **Step 3: Run integration tests**

Run:

```bash
dotnet test tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj --filter "FullyQualifiedName~ToolStoreInitializerTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/ToolStore/ToolStoreInitializer.cs tests/McpGateway.IntegrationTests/ToolStore
git commit -m "feat(tool-store): add ToolStoreInitializer

- Load approved server definitions from PG at startup
- Map to runtime model and populate InMemoryToolStore"
```

---

### Task 5: Add DI registration extension

**Files:**
- Create: `src/McpGateway.Core/CoreServiceExtensions.cs`

- [ ] **Step 1: Implement extension**

Create `src/McpGateway.Core/CoreServiceExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using McpGateway.Core.ToolStore;

namespace McpGateway.Core;

public static class CoreServiceExtensions
{
    public static IServiceCollection AddMcpCore(this IServiceCollection services)
    {
        services.AddSingleton<IToolStore, InMemoryToolStore>();
        services.AddScoped<ToolStoreInitializer>();

        return services;
    }
}
```

- [ ] **Step 2: Add package**

Run:

```bash
dotnet add src/McpGateway.Core/McpGateway.Core.csproj package Microsoft.Extensions.DependencyInjection.Abstractions --version 9.0.4
```

- [ ] **Step 3: Verify build**

Run:

```bash
dotnet build src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/CoreServiceExtensions.cs
git commit -m "feat(tool-store): register Core services in DI

- Add IToolStore as singleton InMemoryToolStore
- Add ToolStoreInitializer as scoped"
```

---

### Task 6: Run full test suite

- [ ] **Step 1: Run all tests**

Run:

```bash
dotnet test /var/home/felix/github/mcp-gateway/McpGateway.sln
```

Expected: Unit and integration tests pass.

- [ ] **Step 2: Commit final state**

```bash
git commit -m "feat(tool-store): complete in-memory tool store and hot reload base

- Runtime McpServerDefinition with ToolDefinition list
- Thread-safe InMemoryToolStore
- Persistence-to-runtime mapper
- ToolStoreInitializer loads approved servers at startup
- DI registration extension"
```

---

## Self-Review

**1. Spec coverage:**

| Requirement | Task |
|---|---|
| In-memory tool store (`ConcurrentDictionary`) | Task 2 |
| Thread-safe add/update/remove | Task 2 |
| Load approved servers from PG on startup | Task 4 |
| Map persistence entities to runtime model | Persistence repository (no separate mapper) |
| Hot-reload API for later admin/refresh use | Task 2 (`AddServer`/`UpdateServer`/`RemoveServer`) |
| DI registration | Task 5 |

**2. Placeholder scan:**

No placeholders. All code and commands are complete.

**3. Type consistency:**

- `McpServerDefinition` is the shared domain model from `McpGateway.Core.ServerDefinitions`.
- `IToolStore` returns `McpServerDefinition?` and `IReadOnlyCollection<McpServerDefinition>`.
- `ToolStoreInitializer` uses `IServerDefinitionRepository.ListApprovedAsync()` which returns the shared domain model directly.

**4. Known follow-ups for Oracle review:**

- Resolved: no duplicate runtime/persistence model. The persistence repository maps EF entities to the shared `McpServerDefinition` domain model.
- `ToolStoreInitializer` relies on `ListApprovedAsync` to include `Tools` and `ToolOverrides` so curated mode and overrides work at runtime.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-07-03-in-memory-tool-store-and-hot-reload.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — Fresh subagent per task, review between tasks.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`.

**Which approach?**
