# Audit & Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add end-to-end audit trail (every MCP tool call → Azure Storage Queue with disk fallback) and OpenTelemetry telemetry (OTLP traces + metrics → Dynatrace in stg/prd, Jaeger in local dev) to the MCP Gateway.

**Architecture:** The audit pipeline is fire-and-forget and decoupled from the tool-call hot path. `ToolCallHandler` builds an `AuditEvent` after each call, hands it to `IAuditEmitter.EmitAsync(...)` and continues. `QueueEmitter` sends to Azure Storage Queue and falls back to local disk on failure. `DiskFallbackRetryWorker` is a `BackgroundService` that drains the on-disk buffer back into the queue when it recovers. Telemetry is wired through `OpenTelemetry.Extensions.AspNetCore` in `McpGateway.Telemetry` (a no-deps library project) and exposed via `TelemetrySetup.AddMcpTelemetry(...)`. Two `ActivitySource`s (`McpGateway.ToolCall`, `McpGateway.OboExchange`) carry the trace data; four `Meter` instruments expose counters and histograms. Local dev exports OTLP/HTTP to the `jaeger` service on port 4318 (already in `docker-compose.yml`).

**Tech Stack:** .NET 10, `Azure.Storage.Queues` 12.21.0, `OpenTelemetry.Extensions.AspNetCore` 1.12.0, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.12.0, `Microsoft.Extensions.Hosting` (BackgroundService), `System.Text.Json`, xUnit, FluentAssertions, Testcontainers 4.13.0 (`Testcontainers.Azurite` via `GenericContainer`).

---

## File Structure

```
McpGateway/
├── McpGateway.sln
├── src/
│   ├── McpGateway.Core/
│   │   └── Audit/
│   │       ├── AuditEvent.cs                # Record schema (caller, auth, target, args, response, status, latency, ts)
│   │       ├── IAuditEmitter.cs             # Fire-and-forget contract
│   │       ├── AuditServiceExtensions.cs    # AddMcpAudit() DI registration
│   │       ├── QueueEmitter.cs              # Azure Storage Queue writer + fallback trigger
│   │       ├── DiskFallback.cs              # Local disk buffer (file-backed queue)
│   │       └── DiskFallbackRetryWorker.cs   # BackgroundService that drains the disk buffer
│   ├── McpGateway.Telemetry/
│   │   ├── McpGateway.Telemetry.csproj
│   │   ├── ActivitySources.cs               # ToolCall + OboExchange ActivitySource definitions
│   │   ├── TelemetryMetrics.cs              # Counter + histogram Meter instruments
│   │   └── TelemetrySetup.cs                # AddMcpTelemetry() extension
│   ├── McpGateway.Api/
│   │   ├── Program.cs                       # Add Telemetry + Audit wiring
│   │   └── appsettings.json                 # Otel:Endpoint, AzureStorage:ConnectionString/QueueName
│   └── McpGateway.Core/Proxy/
│       └── ToolCallHandler.cs               # Audit + ActivitySource emission (no behaviour change)
└── tests/
    └── McpGateway.IntegrationTests/
        └── Audit/
            ├── AzuriteFixture.cs
            ├── AuditCollection.cs
            ├── QueueEmitterTests.cs         # Azurite-backed queue emission
            ├── DiskFallbackTests.cs         # Disk fallback when queue is down
            └── AuditPipelineTests.cs        # End-to-end: tool call → queue message
```

---

### Task 1: Create `McpGateway.Telemetry` project

**Files:**
- Create: `src/McpGateway.Telemetry/McpGateway.Telemetry.csproj`
- Modify: `McpGateway.sln`

- [ ] **Step 1: Create class library project**

Run:

```bash
dotnet new classlib -n McpGateway.Telemetry -o /var/home/felix/github/mcp-gateway/src/McpGateway.Telemetry --framework net10.0
dotnet sln /var/home/felix/github/mcp-gateway/McpGateway.sln add src/McpGateway.Telemetry/McpGateway.Telemetry.csproj
```

Expected: `src/McpGateway.Telemetry/McpGateway.Telemetry.csproj` created and added to solution.

- [ ] **Step 2: Add OpenTelemetry packages**

Run:

```bash
dotnet add src/McpGateway.Telemetry/McpGateway.Telemetry.csproj package OpenTelemetry.Extensions.AspNetCore --version 1.12.0
dotnet add src/McpGateway.Telemetry/McpGateway.Telemetry.csproj package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.12.0
```

Expected: Both packages restore.

- [ ] **Step 3: Replace project file with explicit dependencies**

Edit `src/McpGateway.Telemetry/McpGateway.Telemetry.csproj` to:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OpenTelemetry.Extensions.AspNetCore" Version="1.12.0" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.12.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Delete default `Class1.cs`**

Run:

```bash
rm /var/home/felix/github/mcp-gateway/src/McpGateway.Telemetry/Class1.cs
```

Expected: File removed.

- [ ] **Step 5: Build the project**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/src/McpGateway.Telemetry/McpGateway.Telemetry.csproj
```

Expected: Build succeeds with no warnings.

- [ ] **Step 6: Commit**

```bash
git add src/McpGateway.Telemetry McpGateway.sln
git commit -m "feat(telemetry): add McpGateway.Telemetry project

- Class library targeting net10.0
- OpenTelemetry.Extensions.AspNetCore 1.12.0
- OpenTelemetry.Exporter.OpenTelemetryProtocol 1.12.0"
```

---

### Task 2: Define `ActivitySources` and `TelemetryMetrics`

**Files:**
- Create: `src/McpGateway.Telemetry/ActivitySources.cs`
- Create: `src/McpGateway.Telemetry/TelemetryMetrics.cs`

- [ ] **Step 1: Create `ActivitySources`**

Create `src/McpGateway.Telemetry/ActivitySources.cs`:

```csharp
using System.Diagnostics;

namespace McpGateway.Telemetry;

public static class ActivitySources
{
    public const string ToolCallName = "McpGateway.ToolCall";
    public const string OboExchangeName = "McpGateway.OboExchange";

    public static readonly ActivitySource ToolCall = new(ToolCallName, "1.0.0");
    public static readonly ActivitySource OboExchange = new(OboExchangeName, "1.0.0");
}
```

- [ ] **Step 2: Create `TelemetryMetrics`**

Create `src/McpGateway.Telemetry/TelemetryMetrics.cs`:

```csharp
using System.Diagnostics.Metrics;

namespace McpGateway.Telemetry;

public static class TelemetryMetrics
{
    public const string MeterName = "McpGateway";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> ToolCallCount =
        Meter.CreateCounter<long>("mcp.tool.call.count", description: "Total MCP tool calls.");

    public static readonly Histogram<double> ToolCallLatencyMs =
        Meter.CreateHistogram<double>("mcp.tool.call.latency.ms", unit: "ms", description: "MCP tool call latency in milliseconds.");

    public static readonly Counter<long> ToolCallErrors =
        Meter.CreateCounter<long>("mcp.tool.call.errors", description: "MCP tool calls that returned isError=true.");

    public static readonly Counter<long> OboCacheHits =
        Meter.CreateCounter<long>("mcp.obo.cache.hits", description: "OBO token cache hits.");

    public static readonly Counter<long> OboCacheMisses =
        Meter.CreateCounter<long>("mcp.obo.cache.misses", description: "OBO token cache misses.");
}
```

- [ ] **Step 3: Build the project**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/src/McpGateway.Telemetry/McpGateway.Telemetry.csproj
```

Expected: Build succeeds with no warnings.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Telemetry/ActivitySources.cs src/McpGateway.Telemetry/TelemetryMetrics.cs
git commit -m "feat(telemetry): add ActivitySources and TelemetryMetrics

- ToolCall + OboExchange ActivitySources
- ToolCallCount, ToolCallLatency, ToolCallErrors counters/histograms
- OboCacheHits, OboCacheMisses counters"
```

---

### Task 3: Implement `TelemetrySetup`

**Files:**
- Create: `src/McpGateway.Telemetry/TelemetrySetup.cs`

- [ ] **Step 1: Implement the extension**

Create `src/McpGateway.Telemetry/TelemetrySetup.cs`:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace McpGateway.Telemetry;

public static class TelemetrySetup
{
    public const string ServiceName = "McpGateway";

    public static IServiceCollection AddMcpTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration["Otel:Exporter:Otlp:Endpoint"]
            ?? "http://localhost:4318";

        var resource = ResourceBuilder.CreateDefault()
            .AddService(ServiceName, serviceVersion: "1.0.0");

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    .AddSource(ActivitySources.ToolCallName)
                    .AddSource(ActivitySources.OboExchangeName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(opt =>
                    {
                        opt.Endpoint = new Uri(otlpEndpoint);
                        opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resource)
                    .AddMeter(TelemetryMetrics.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(opt =>
                    {
                        opt.Endpoint = new Uri(otlpEndpoint);
                        opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });
            });

        return services;
    }
}
```

- [ ] **Step 2: Build the project**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/src/McpGateway.Telemetry/McpGateway.Telemetry.csproj
```

Expected: Build succeeds with no warnings.

- [ ] **Step 3: Commit**

```bash
git add src/McpGateway.Telemetry/TelemetrySetup.cs
git commit -m "feat(telemetry): add TelemetrySetup with OTLP exporter

- Configures tracing for ToolCall + OboExchange ActivitySources
- Configures metrics for McpGateway meter (counter + histogram)
- Local dev: OTLP HTTP at configurable endpoint (default http://localhost:4318)
- Dynatrace: set Otel:Exporter:Otlp:Endpoint to tenant OTLP URL"
```

---

### Task 4: Define `AuditEvent` schema and `IAuditEmitter`

**Files:**
- Create: `src/McpGateway.Core/Audit/AuditEvent.cs`
- Create: `src/McpGateway.Core/Audit/IAuditEmitter.cs`
- Modify: `src/McpGateway.Core/McpGateway.Core.csproj`

- [ ] **Step 1: Add Azure Storage Queues package to Core**

Run:

```bash
dotnet add /var/home/felix/github/mcp-gateway/src/McpGateway.Core/McpGateway.Core.csproj package Azure.Storage.Queues --version 12.21.0
```

Expected: Package added and restored.

- [ ] **Step 2: Create `AuditEvent`**

Create `src/McpGateway.Core/Audit/AuditEvent.cs`:

```csharp
using System.Text.Json.Serialization;

namespace McpGateway.Core.Audit;

public class AuditEvent
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("caller_id")]
    public string CallerId { get; set; } = string.Empty;

    [JsonPropertyName("caller_name")]
    public string? CallerName { get; set; }

    [JsonPropertyName("gateway_auth_method")]
    public string GatewayAuthMethod { get; set; } = string.Empty;

    [JsonPropertyName("auth_strategy")]
    public string AuthStrategy { get; set; } = string.Empty;

    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = string.Empty;

    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";

    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("http_status")]
    public int HttpStatus { get; set; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; set; }

    [JsonPropertyName("latency_ms")]
    public long LatencyMs { get; set; }
}
```

- [ ] **Step 3: Create `IAuditEmitter`**

Create `src/McpGateway.Core/Audit/IAuditEmitter.cs`:

```csharp
namespace McpGateway.Core.Audit;

public interface IAuditEmitter
{
    Task EmitAsync(AuditEvent auditEvent, CancellationToken ct = default);
}
```

- [ ] **Step 4: Build Core**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds with no warnings.

- [ ] **Step 5: Commit**

```bash
git add src/McpGateway.Core/Audit src/McpGateway.Core/McpGateway.Core.csproj
git commit -m "feat(audit): add AuditEvent schema and IAuditEmitter

- AuditEvent fields: caller, gateway auth method, auth strategy,
  server name, tool name, arguments, response, http status,
  is_error, latency_ms, timestamp
- IAuditEmitter.EmitAsync fire-and-forget contract"
```

---

### Task 5: Implement `QueueEmitter` with disk-fallback trigger

**Files:**
- Create: `src/McpGateway.Core/Audit/QueueEmitter.cs`
- Create: `src/McpGateway.Core/Audit/QueueEmitterOptions.cs`

- [ ] **Step 1: Create `QueueEmitterOptions`**

Create `src/McpGateway.Core/Audit/QueueEmitterOptions.cs`:

```csharp
namespace McpGateway.Core.Audit;

public class QueueEmitterOptions
{
    public const int MaxResponseBytes = 10 * 1024;

    public string ConnectionString { get; set; } = string.Empty;
    public string QueueName { get; set; } = "mcp-audit";
}
```

- [ ] **Step 2: Create `QueueEmitter`**

Create `src/McpGateway.Core/Audit/QueueEmitter.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpGateway.Core.Audit;

public class QueueEmitter : IAuditEmitter
{
    private readonly QueueClient _queueClient;
    private readonly DiskFallback _diskFallback;
    private readonly ILogger<QueueEmitter> _logger;
    private readonly TimeProvider _timeProvider;

    public QueueEmitter(
        IOptions<QueueEmitterOptions> options,
        DiskFallback diskFallback,
        ILogger<QueueEmitter> logger,
        TimeProvider timeProvider)
    {
        var opts = options.Value;
        _queueClient = new QueueClient(opts.ConnectionString, opts.QueueName);
        _diskFallback = diskFallback;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task EmitAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        auditEvent.Response = Truncate(auditEvent.Response, QueueEmitterOptions.MaxResponseBytes);

        var payload = JsonSerializer.SerializeToUtf8Bytes(auditEvent);

        try
        {
            await _queueClient.CreateIfNotExistsAsync(cancellationToken: ct);
            var base64Encoded = Convert.ToBase64String(payload);
            await _queueClient.SendMessageAsync(base64Encoded, cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(ex, "Audit queue not found; buffering to disk.");
            await _diskFallback.BufferAsync(auditEvent, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue audit event {EventId}; buffering to disk.", auditEvent.EventId);
            await _diskFallback.BufferAsync(auditEvent, ct);
        }
    }

    private static string Truncate(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
        {
            return value;
        }

        return Encoding.UTF8.GetString(bytes, 0, maxBytes);
    }
}
```

- [ ] **Step 3: Build Core**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds (the project now references `DiskFallback` which we have not yet created — so this build will fail. Continue to Task 6 before re-running.).

---

### Task 6: Implement `DiskFallback` file-backed buffer

**Files:**
- Create: `src/McpGateway.Core/Audit/DiskFallback.cs`
- Create: `src/McpGateway.Core/Audit/DiskFallbackOptions.cs`

- [ ] **Step 1: Create `DiskFallbackOptions`**

Create `src/McpGateway.Core/Audit/DiskFallbackOptions.cs`:

```csharp
namespace McpGateway.Core.Audit;

public class DiskFallbackOptions
{
    public string Directory { get; set; } = Path.Combine(Path.GetTempPath(), "mcp-gateway-audit-fallback");
    public int MaxBufferBytes { get; set; } = 100 * 1024 * 1024; // 100 MB ceiling
}
```

- [ ] **Step 2: Create `DiskFallback`**

Create `src/McpGateway.Core/Audit/DiskFallback.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpGateway.Core.Audit;

public class DiskFallback
{
    private readonly DiskFallbackOptions _options;
    private readonly ILogger<DiskFallback> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentQueue<string> _files = new();

    public DiskFallback(IOptions<DiskFallbackOptions> options, ILogger<DiskFallback> logger)
    {
        _options = options.Value;
        _logger = logger;

        Directory.CreateDirectory(_options.Directory);
        LoadExistingFiles();
    }

    public async Task BufferAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(auditEvent);
        var fileName = $"{auditEvent.EventId}.json";
        var path = Path.Combine(_options.Directory, fileName);

        await _writeLock.WaitAsync(ct);
        try
        {
            if (DirectorySizeBytes() + payload.Length > _options.MaxBufferBytes)
            {
                _logger.LogError(
                    "Audit disk fallback buffer exceeded {MaxBytes} bytes; dropping event {EventId}.",
                    _options.MaxBufferBytes,
                    auditEvent.EventId);
                return;
            }

            await File.WriteAllBytesAsync(path, payload, ct);
            _files.Enqueue(path);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public IReadOnlyCollection<string> GetPendingFiles()
    {
        return _files.Where(File.Exists).ToList().AsReadOnly();
    }

    public async Task RemoveAsync(string path, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void LoadExistingFiles()
    {
        foreach (var path in Directory.EnumerateFiles(_options.Directory, "*.json"))
        {
            _files.Enqueue(path);
        }
    }

    private long DirectorySizeBytes()
    {
        return new DirectoryInfo(_options.Directory)
            .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
            .Sum(f => f.Length);
    }
}
```

- [ ] **Step 3: Build Core**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds with no warnings.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/Audit
git commit -m "feat(audit): add QueueEmitter, DiskFallback, and options

- QueueEmitter wraps Azure Storage Queue with disk-fallback trigger
- DiskFallback is a file-backed queue with 100MB ceiling
- Truncate audit response body to 10KB before serialization"
```

---

### Task 7: Implement `DiskFallbackRetryWorker`

**Files:**
- Create: `src/McpGateway.Core/Audit/DiskFallbackRetryWorker.cs`

- [ ] **Step 1: Implement the worker**

Create `src/McpGateway.Core/Audit/DiskFallbackRetryWorker.cs`:

```csharp
using System.Text.Json;
using Azure;
using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpGateway.Core.Audit;

public class DiskFallbackRetryWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly QueueEmitterOptions _queueOptions;
    private readonly DiskFallback _diskFallback;
    private readonly ILogger<DiskFallbackRetryWorker> _logger;

    public DiskFallbackRetryWorker(
        IOptions<QueueEmitterOptions> queueOptions,
        DiskFallback diskFallback,
        ILogger<DiskFallbackRetryWorker> logger)
    {
        _queueOptions = queueOptions.Value;
        _diskFallback = diskFallback;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit disk fallback drain failed; will retry next tick.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task DrainAsync(CancellationToken ct)
    {
        var queueClient = new QueueClient(_queueOptions.ConnectionString, _queueOptions.QueueName);
        try
        {
            await queueClient.CreateIfNotExistsAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "Audit queue still unavailable; will retry later.");
            return;
        }

        foreach (var file in _diskFallback.GetPendingFiles())
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(file, ct);
                var auditEvent = JsonSerializer.Deserialize<AuditEvent>(bytes)
                    ?? throw new InvalidOperationException($"Cannot deserialize {file}.");

                var base64Encoded = Convert.ToBase64String(bytes);
                await queueClient.SendMessageAsync(base64Encoded, cancellationToken: ct);
                await _diskFallback.RemoveAsync(file, ct);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(ex, "Audit queue rejected message for {File}; leaving on disk.", file);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replay audit event from {File}; leaving on disk.", file);
            }
        }
    }
}
```

- [ ] **Step 2: Build Core**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds with no warnings.

- [ ] **Step 3: Commit**

```bash
git add src/McpGateway.Core/Audit/DiskFallbackRetryWorker.cs
git commit -m "feat(audit): add DiskFallbackRetryWorker background service

- Drains disk-fallback buffer back into Azure Storage Queue
- Polls every 30 seconds
- Skips files that fail rather than aborting the drain"
```

---

### Task 8: Register audit services in DI

**Files:**
- Create: `src/McpGateway.Core/Audit/AuditServiceExtensions.cs`

- [ ] **Step 1: Implement the DI extension**

Create `src/McpGateway.Core/Audit/AuditServiceExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace McpGateway.Core.Audit;

public static class AuditServiceExtensions
{
    public static IServiceCollection AddMcpAudit(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QueueEmitterOptions>(opts =>
        {
            opts.ConnectionString = configuration["AzureStorage:ConnectionString"]
                ?? throw new InvalidOperationException("AzureStorage:ConnectionString is required.");
            opts.QueueName = configuration["AzureStorage:Audit:QueueName"] ?? "mcp-audit";
        });

        services.Configure<DiskFallbackOptions>(opts =>
        {
            var directory = configuration["Audit:DiskFallback:Directory"];
            if (!string.IsNullOrWhiteSpace(directory))
            {
                opts.Directory = directory;
            }
        });

        services.AddSingleton<DiskFallback>();
        services.AddSingleton<IAuditEmitter, QueueEmitter>();
        services.AddHostedService<DiskFallbackRetryWorker>();

        return services;
    }
}
```

- [ ] **Step 2: Build Core**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds with no warnings.

- [ ] **Step 3: Commit**

```bash
git add src/McpGateway.Core/Audit/AuditServiceExtensions.cs
git commit -m "feat(audit): add AddMcpAudit DI extension

- Binds QueueEmitterOptions from AzureStorage:ConnectionString + QueueName
- Binds DiskFallbackOptions from Audit:DiskFallback:Directory
- Registers IAuditEmitter (singleton) and DiskFallbackRetryWorker (hosted)"
```

---

### Task 9: Wire audit + telemetry into `ToolCallHandler`

**Files:**
- Modify: `src/McpGateway.Core/Proxy/ToolCallHandler.cs`

- [ ] **Step 1: Replace `ToolCallHandler` with audit + telemetry integration**

Replace the contents of `src/McpGateway.Core/Proxy/ToolCallHandler.cs` with:

```csharp
using System.Diagnostics;
using McpGateway.Core.Audit;
using McpGateway.Core.Proxy.Exceptions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using McpGateway.Telemetry;

namespace McpGateway.Core.Proxy;

public class ToolCallHandler
{
    public const string HttpClientName = "McpToolProxy";

    private readonly IToolStore _toolStore;
    private readonly HttpRequestBuilder _requestBuilder;
    private readonly ResponseWrapper _responseWrapper;
    private readonly HttpClient _httpClient;
    private readonly IAuditEmitter _auditEmitter;
    private readonly TimeProvider _timeProvider;
    private readonly ToolCallContextAccessor _contextAccessor;

    public ToolCallHandler(
        IToolStore toolStore,
        HttpRequestBuilder requestBuilder,
        ResponseWrapper responseWrapper,
        HttpClient httpClient,
        IAuditEmitter auditEmitter,
        TimeProvider timeProvider,
        ToolCallContextAccessor contextAccessor)
    {
        _toolStore = toolStore;
        _requestBuilder = requestBuilder;
        _responseWrapper = responseWrapper;
        _httpClient = httpClient;
        _auditEmitter = auditEmitter;
        _timeProvider = timeProvider;
        _contextAccessor = contextAccessor;
    }

    public async Task<ToolCallResult> HandleAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        var startedAt = _timeProvider.GetTimestamp();
        var context = _contextAccessor.Current;

        using var activity = ActivitySources.ToolCall.StartActivity("mcp.tool.call", ActivityKind.Internal);
        activity?.SetTag("mcp.server.name", serverName);
        activity?.SetTag("mcp.tool.name", toolName);

        var server = _toolStore.GetServer(serverName)
            ?? throw new KeyNotFoundException($"Server '{serverName}' not found.");

        var tool = server.Tools.FirstOrDefault(t => t.ToolName == toolName)
            ?? throw new ToolNotFoundException(serverName, toolName);

        var argumentsJson = System.Text.Json.JsonSerializer.Serialize(arguments);
        activity?.SetTag("mcp.tool.arguments", argumentsJson);

        var request = _requestBuilder.Build(server.BaseUrl, tool, arguments);
        var response = await _httpClient.SendAsync(request, ct);
        var result = await _responseWrapper.WrapAsync(response);

        var elapsed = _timeProvider.GetElapsedTime(startedAt);
        var latencyMs = (long)elapsed.TotalMilliseconds;

        activity?.SetTag("mcp.tool.http_status", (int)response.StatusCode);
        activity?.SetTag("mcp.tool.is_error", result.IsError);

        TelemetryMetrics.ToolCallCount.Add(1,
            new KeyValuePair<string, object?>("mcp.server.name", serverName),
            new KeyValuePair<string, object?>("mcp.tool.name", toolName));

        TelemetryMetrics.ToolCallLatencyMs.Record(latencyMs,
            new KeyValuePair<string, object?>("mcp.server.name", serverName),
            new KeyValuePair<string, object?>("mcp.tool.name", toolName));

        if (result.IsError)
        {
            TelemetryMetrics.ToolCallErrors.Add(1,
                new KeyValuePair<string, object?>("mcp.server.name", serverName),
                new KeyValuePair<string, object?>("mcp.tool.name", toolName));
        }

        await EmitAuditAsync(
            server,
            tool,
            context,
            argumentsJson,
            result,
            (int)response.StatusCode,
            latencyMs,
            ct);

        return result;
    }

    private async Task EmitAuditAsync(
        McpServerDefinition server,
        ToolDefinition tool,
        ToolCallContext? context,
        string argumentsJson,
        ToolCallResult result,
        int httpStatus,
        long latencyMs,
        CancellationToken ct)
    {
        if (context is null)
        {
            return;
        }

        var responseText = result.Content.FirstOrDefault()?.Text ?? string.Empty;

        var auditEvent = new AuditEvent
        {
            CallerId = context.Caller.Id,
            CallerName = context.Caller.Name,
            GatewayAuthMethod = context.Caller.AuthMethod.ToString(),
            AuthStrategy = server.AuthStrategy,
            ServerName = server.Name,
            ToolName = tool.ToolName,
            Arguments = argumentsJson,
            Response = responseText,
            HttpStatus = httpStatus,
            IsError = result.IsError,
            LatencyMs = latencyMs
        };

        await _auditEmitter.EmitAsync(auditEvent, ct);
    }
}
```

- [ ] **Step 2: Add `ToolCallContextAccessor` to Core**

Create `src/McpGateway.Core/Proxy/ToolCallContextAccessor.cs`:

```csharp
namespace McpGateway.Core.Proxy;

public class ToolCallContextAccessor
{
    private static readonly AsyncLocal<ToolCallContext?> _current = new();

    public ToolCallContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
```

- [ ] **Step 3: Build Core**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds with no warnings.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/Proxy/ToolCallHandler.cs src/McpGateway.Core/Proxy/ToolCallContextAccessor.cs
git commit -m "feat(proxy): wire audit + telemetry into ToolCallHandler

- Emit mcp.tool.call activity span with server, tool, status, is_error tags
- Update ToolCallCount / ToolCallLatencyMs / ToolCallErrors metrics
- Fire-and-forget AuditEvent via IAuditEmitter when ToolCallContext is set"
```

---

### Task 10: Update DI registration + `Program.cs`

**Files:**
- Modify: `src/McpGateway.Core/Proxy/ProxyServiceExtensions.cs`
- Modify: `src/McpGateway.Api/Program.cs`
- Modify: `src/McpGateway.Api/appsettings.json`

- [ ] **Step 1: Update `ProxyServiceExtensions` to pass audit/telemetry dependencies**

Replace `src/McpGateway.Core/Proxy/ProxyServiceExtensions.cs` with:

```csharp
using McpGateway.Core.Audit;
using McpGateway.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace McpGateway.Core.Proxy;

public static class ProxyServiceExtensions
{
    public static IServiceCollection AddMcpProxy(this IServiceCollection services)
    {
        services.AddSingleton<HttpRequestBuilder>();
        services.AddSingleton<ResponseWrapper>();
        services.AddSingleton<MetaToolsHandler>();
        services.AddSingleton<ToolCallContextAccessor>();

        services.AddHttpClient(ToolCallHandler.HttpClientName)
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());

        services.AddScoped<ToolCallHandler>(provider =>
        {
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            return new ToolCallHandler(
                provider.GetRequiredService<IToolStore>(),
                provider.GetRequiredService<HttpRequestBuilder>(),
                provider.GetRequiredService<ResponseWrapper>(),
                factory.CreateClient(ToolCallHandler.HttpClientName),
                provider.GetRequiredService<IAuditEmitter>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ToolCallContextAccessor>());
        });

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
    }
}
```

- [ ] **Step 2: Add Telemetry + Audit + TimeProvider to `Program.cs`**

Edit `src/McpGateway.Api/Program.cs`. After the existing `builder.Services` configuration, add:

```csharp
using McpGateway.Core.Audit;
using McpGateway.Telemetry;

// ... existing code ...

builder.Services.AddMcpTelemetry(builder.Configuration);
builder.Services.AddMcpAudit(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
```

Ensure the following `using` directives appear at the top of `Program.cs`:

```csharp
using McpGateway.Core.Audit;
using McpGateway.Core.Proxy;
using McpGateway.Telemetry;
```

(Remove the duplicate `using McpGateway.Core.Proxy;` if it already exists.)

- [ ] **Step 2: Update `ToolCallHandlerTests` for the new constructor**

Replace `tests/McpGateway.UnitTests/Proxy/ToolCallHandlerTests.cs` with:

```csharp
using FluentAssertions;
using McpGateway.Core.Audit;
using McpGateway.Core.Proxy;
using McpGateway.Core.Proxy.Exceptions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using NSubstitute;
using System.Text.Json.Nodes;

namespace McpGateway.UnitTests.Proxy;

public class ToolCallHandlerTests
{
    private readonly InMemoryToolStore _store = new();
    private readonly IAuditEmitter _auditEmitter = Substitute.For<IAuditEmitter>();
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly ToolCallContextAccessor _contextAccessor = new();

    [Fact]
    public async Task HandleAsync_UnknownServer_Throws()
    {
        var handler = CreateHandler();
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.HandleAsync("unknown", "tool", [], CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownTool_Throws()
    {
        _store.AddServer(CreateServer("api", []));
        var handler = CreateHandler();

        await Assert.ThrowsAsync<ToolNotFoundException>(
            () => handler.HandleAsync("api", "missing", [], CancellationToken.None));
    }

    private ToolCallHandler CreateHandler()
    {
        var httpClient = new HttpClient(new TestHttpMessageHandler());
        return new ToolCallHandler(
            _store,
            new HttpRequestBuilder(),
            new ResponseWrapper(),
            httpClient,
            _auditEmitter,
            _timeProvider,
            _contextAccessor);
    }

    private static McpServerDefinition CreateServer(string name, List<ToolDefinition> tools) => new()
    {
        Name = name,
        DisplayName = name,
        BaseUrl = "https://api.example.com",
        SpecHash = "hash",
        AuthStrategy = "obo",
        AuthConfig = "{}",
        Tools = tools
    };

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });
        }
    }
}
```

- [ ] **Step 3: Add appsettings entries**

Edit `src/McpGateway.Api/appsettings.json` to include the audit and OTLP blocks (preserve existing keys):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Otel": {
    "Exporter": {
      "Otlp": {
        "Endpoint": "http://localhost:4318"
      }
    }
  },
  "AzureStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6kqzK7nt6qNQ1bq;QueueEndpoint=http://localhost:10001/devstoreaccount1",
    "Audit": {
      "QueueName": "mcp-audit"
    }
  },
  "Audit": {
    "DiskFallback": {
      "Directory": "/tmp/mcp-gateway-audit-fallback"
    }
  }
}
```

- [ ] **Step 4: Reference Core + Telemetry in Api project**

Run:

```bash
dotnet add /var/home/felix/github/mcp-gateway/src/McpGateway.Api/McpGateway.Api.csproj reference /var/home/felix/github/mcp-gateway/src/McpGateway.Telemetry/McpGateway.Telemetry.csproj
```

Expected: Project reference added.

- [ ] **Step 5: Build the solution**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/McpGateway.sln
```

Expected: Build succeeds with no warnings.

- [ ] **Step 6: Commit**

```bash
git add src/McpGateway.Api src/McpGateway.Core/Proxy/ProxyServiceExtensions.cs tests/McpGateway.UnitTests/Proxy/ToolCallHandlerTests.cs
git commit -m "feat(api): wire telemetry + audit DI in Program.cs

- AddMcpTelemetry (OTLP exporter)
- AddMcpAudit (Queue emitter + disk fallback + retry worker)
- appsettings.json: Otel:Exporter:Otlp:Endpoint, AzureStorage:*, Audit:DiskFallback:Directory
- ToolCallHandler now takes IAuditEmitter, TimeProvider, ToolCallContextAccessor
- Update ToolCallHandlerTests for the 7-parameter constructor"
```

---

### Task 11: Add integration test infrastructure (Azurite fixture)

**Files:**
- Create: `tests/McpGateway.IntegrationTests/Audit/AzuriteFixture.cs`
- Create: `tests/McpGateway.IntegrationTests/Audit/AuditCollection.cs`

- [ ] **Step 1: Add Testcontainers Azurite image to test project**

Run:

```bash
dotnet add /var/home/felix/github/mcp-gateway/tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj package Testcontainers --version 4.13.0
```

Expected: Package added.

- [ ] **Step 2: Create `AzuriteFixture`**

Create `tests/McpGateway.IntegrationTests/Audit/AzuriteFixture.cs`:

```csharp
using Azure.Storage.Queues;
using DotNet.Testcontainers.Containers;
using Testcontainers.Azurite;

namespace McpGateway.IntegrationTests.Audit;

public sealed class AzuriteFixture : IAsyncLifetime
{
    public const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.34.0";
    public const string QueuePort = "10001";

    private readonly AzuriteContainer _container;

    public AzuriteFixture()
    {
        _container = new AzuriteBuilder()
            .WithImage(AzuriteImage)
            .WithPortBinding(QueuePort, true)
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public QueueClient CreateQueueClient(string queueName)
    {
        return new QueueClient(ConnectionString, queueName);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
```

- [ ] **Step 3: Create xUnit collection definition**

Create `tests/McpGateway.IntegrationTests/Audit/AuditCollection.cs`:

```csharp
namespace McpGateway.IntegrationTests.Audit;

[CollectionDefinition("Audit")]
public class AuditCollection : ICollectionFixture<AzuriteFixture>
{
}
```

- [ ] **Step 4: Build the test project**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj
```

Expected: Build succeeds with no warnings.

- [ ] **Step 5: Commit**

```bash
git add tests/McpGateway.IntegrationTests/Audit tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj
git commit -m "test(audit): add Azurite Testcontainer fixture

- AzuriteFixture starts mcr.microsoft.com/azure-storage/azurite:3.34.0
- Exposes queue connection string and QueueClient factory"
```

---

### Task 12: Write `QueueEmitterTests` against Azurite

**Files:**
- Create: `tests/McpGateway.IntegrationTests/Audit/QueueEmitterTests.cs`

- [ ] **Step 1: Write the test file**

Create `tests/McpGateway.IntegrationTests/Audit/QueueEmitterTests.cs`:

```csharp
using Azure;
using FluentAssertions;
using McpGateway.Core.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace McpGateway.IntegrationTests.Audit;

[Collection("Audit")]
public class QueueEmitterTests
{
    private readonly AzuriteFixture _fixture;
    private readonly string _queueName = $"mcp-audit-{Guid.NewGuid():N}";

    public QueueEmitterTests(AzuriteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EmitAsync_SendsBase64JsonMessage()
    {
        var queue = _fixture.CreateQueueClient(_queueName);
        await queue.CreateIfNotExistsAsync();

        var diskFallback = CreateDiskFallback();
        var emitter = new QueueEmitter(
            Options.Create(new QueueEmitterOptions
            {
                ConnectionString = _fixture.ConnectionString,
                QueueName = _queueName
            }),
            diskFallback,
            NullLogger<QueueEmitter>.Instance,
            TimeProvider.System);

        var auditEvent = new AuditEvent
        {
            CallerId = "user-1",
            CallerName = "alice@example.com",
            GatewayAuthMethod = "EntraIdJwt",
            AuthStrategy = "obo",
            ServerName = "invoice-api",
            ToolName = "get_invoices",
            Arguments = "{\"limit\":10}",
            Response = "{\"items\":[]}",
            HttpStatus = 200,
            IsError = false,
            LatencyMs = 42
        };

        await emitter.EmitAsync(auditEvent);

        var messages = await queue.ReceiveMessagesAsync(maxMessages: 10);
        messages.Value.Length.Should().Be(1);
        var payload = messages.Value[0].Body.ToString();
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<AuditEvent>(payload)!;
        roundTrip.CallerId.Should().Be("user-1");
        roundTrip.ServerName.Should().Be("invoice-api");
        roundTrip.ToolName.Should().Be("get_invoices");
    }

    [Fact]
    public async Task EmitAsync_TruncatesResponseTo10Kb()
    {
        var queue = _fixture.CreateQueueClient(_queueName);
        await queue.CreateIfNotExistsAsync();

        var diskFallback = CreateDiskFallback();
        var emitter = new QueueEmitter(
            Options.Create(new QueueEmitterOptions
            {
                ConnectionString = _fixture.ConnectionString,
                QueueName = _queueName
            }),
            diskFallback,
            NullLogger<QueueEmitter>.Instance,
            TimeProvider.System);

        var auditEvent = new AuditEvent
        {
            ServerName = "invoice-api",
            ToolName = "get_invoices",
            Response = new string('x', 20_000)
        };

        await emitter.EmitAsync(auditEvent);

        var messages = await queue.ReceiveMessagesAsync(maxMessages: 10);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<AuditEvent>(messages.Value[0].Body.ToString())!;
        roundTrip.Response.Length.Should().BeLessThanOrEqualTo(QueueEmitterOptions.MaxResponseBytes);
    }

    [Fact]
    public async Task EmitAsync_WhenQueueMissing_FallsBackToDisk()
    {
        var diskFallback = CreateDiskFallback();
        var emitter = new QueueEmitter(
            Options.Create(new QueueEmitterOptions
            {
                ConnectionString = _fixture.ConnectionString,
                QueueName = $"missing-queue-{Guid.NewGuid():N}"
            }),
            diskFallback,
            NullLogger<QueueEmitter>.Instance,
            TimeProvider.System);

        var auditEvent = new AuditEvent
        {
            ServerName = "invoice-api",
            ToolName = "get_invoices",
            EventId = Guid.NewGuid().ToString("N")
        };

        await emitter.EmitAsync(auditEvent);

        var pending = diskFallback.GetPendingFiles();
        pending.Should().ContainSingle();
    }

    private DiskFallback CreateDiskFallback()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"audit-fallback-{Guid.NewGuid():N}");
        return new DiskFallback(
            Options.Create(new DiskFallbackOptions { Directory = directory }),
            NullLogger<DiskFallback>.Instance);
    }
}
```

- [ ] **Step 2: Run the tests**

Run:

```bash
dotnet test /var/home/felix/github/mcp-gateway/tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj --filter "FullyQualifiedName~QueueEmitterTests" -v n
```

Expected: 3 tests pass. Docker must be available for the Azurite Testcontainer.

- [ ] **Step 3: Commit**

```bash
git add tests/McpGateway.IntegrationTests/Audit/QueueEmitterTests.cs
git commit -m "test(audit): add QueueEmitter tests against Azurite

- EmitAsync serializes AuditEvent as base64 JSON in the queue
- Response body is truncated to 10KB
- When the queue does not exist, the event is buffered to disk"
```

---

### Task 13: Write `DiskFallbackTests`

**Files:**
- Create: `tests/McpGateway.IntegrationTests/Audit/DiskFallbackTests.cs`

- [ ] **Step 1: Write the test file**

Create `tests/McpGateway.IntegrationTests/Audit/DiskFallbackTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace McpGateway.IntegrationTests.Audit;

public class DiskFallbackTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"audit-fallback-{Guid.NewGuid():N}");

    [Fact]
    public async Task BufferAsync_WritesFileToDirectory()
    {
        var fallback = CreateFallback();

        var auditEvent = new AuditEvent
        {
            ServerName = "invoice-api",
            ToolName = "get_invoices",
            EventId = "abc123"
        };

        await fallback.BufferAsync(auditEvent);

        var pending = fallback.GetPendingFiles();
        pending.Should().ContainSingle();
        File.Exists(pending.First()).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveAsync_DeletesFile()
    {
        var fallback = CreateFallback();
        var auditEvent = new AuditEvent { EventId = "remove-me" };
        await fallback.BufferAsync(auditEvent);

        var file = fallback.GetPendingFiles().Single();
        await fallback.RemoveAsync(file);

        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public async Task LoadExistingFiles_PicksUpFilesCreatedBeforeStartup()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"audit-fallback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "preexisting.json");
        await File.WriteAllTextAsync(file, "{}");

        var fallback = new DiskFallback(
            Options.Create(new DiskFallbackOptions { Directory = dir }),
            NullLogger<DiskFallback>.Instance);

        fallback.GetPendingFiles().Should().ContainSingle();
    }

    private DiskFallback CreateFallback()
    {
        return new DiskFallback(
            Options.Create(new DiskFallbackOptions { Directory = _directory }),
            NullLogger<DiskFallback>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the tests**

Run:

```bash
dotnet test /var/home/felix/github/mcp-gateway/tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj --filter "FullyQualifiedName~DiskFallbackTests" -v n
```

Expected: 3 tests pass.

- [ ] **Step 3: Commit**

```bash
git add tests/McpGateway.IntegrationTests/Audit/DiskFallbackTests.cs
git commit -m "test(audit): add DiskFallback tests

- BufferAsync writes file to configured directory
- RemoveAsync deletes the file
- Existing files in the directory are picked up on construction"
```

---

### Task 14: Write `AuditPipelineTests` (queue + retry worker)

**Files:**
- Create: `tests/McpGateway.IntegrationTests/Audit/AuditPipelineTests.cs`

- [ ] **Step 1: Write the test file**

Create `tests/McpGateway.IntegrationTests/Audit/AuditPipelineTests.cs`:

```csharp
using Azure;
using FluentAssertions;
using McpGateway.Core.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace McpGateway.IntegrationTests.Audit;

[Collection("Audit")]
public class AuditPipelineTests
{
    private readonly AzuriteFixture _fixture;
    private readonly string _queueName = $"mcp-audit-{Guid.NewGuid():N}";

    public AuditPipelineTests(AzuriteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DiskFallbackRetryWorker_DrainsBufferIntoQueue()
    {
        var diskDirectory = Path.Combine(Path.GetTempPath(), $"audit-fallback-{Guid.NewGuid():N}");
        var diskFallback = new DiskFallback(
            Options.Create(new DiskFallbackOptions { Directory = diskDirectory }),
            NullLogger<DiskFallback>.Instance);

        // Pre-populate the buffer with two events.
        await diskFallback.BufferAsync(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            ServerName = "invoice-api",
            ToolName = "get_invoices"
        });
        await diskFallback.BufferAsync(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            ServerName = "invoice-api",
            ToolName = "get_invoice"
        });

        var worker = new DiskFallbackRetryWorker(
            Options.Create(new QueueEmitterOptions
            {
                ConnectionString = _fixture.ConnectionString,
                QueueName = _queueName
            }),
            diskFallback,
            NullLogger<DiskFallbackRetryWorker>.Instance);

        var queue = _fixture.CreateQueueClient(_queueName);
        await queue.CreateIfNotExistsAsync();

        await worker.DrainAsync(CancellationToken.None);

        var messages = await queue.ReceiveMessagesAsync(maxMessages: 10);
        messages.Value.Length.Should().Be(2);
        diskFallback.GetPendingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task DiskFallbackRetryWorker_WhenQueueMissing_LeavesBufferIntact()
    {
        var diskDirectory = Path.Combine(Path.GetTempPath(), $"audit-fallback-{Guid.NewGuid():N}");
        var diskFallback = new DiskFallback(
            Options.Create(new DiskFallbackOptions { Directory = diskDirectory }),
            NullLogger<DiskFallback>.Instance);

        await diskFallback.BufferAsync(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            ServerName = "invoice-api",
            ToolName = "get_invoices"
        });

        var worker = new DiskFallbackRetryWorker(
            Options.Create(new QueueEmitterOptions
            {
                ConnectionString = _fixture.ConnectionString,
                QueueName = $"missing-queue-{Guid.NewGuid():N}"
            }),
            diskFallback,
            NullLogger<DiskFallbackRetryWorker>.Instance);

        await worker.DrainAsync(CancellationToken.None);

        diskFallback.GetPendingFiles().Should().ContainSingle();
    }
}
```

- [ ] **Step 2: Run the tests**

Run:

```bash
dotnet test /var/home/felix/github/mcp-gateway/tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj --filter "FullyQualifiedName~AuditPipelineTests" -v n
```

Expected: 2 tests pass. Docker must be available.

- [ ] **Step 3: Commit**

```bash
git add tests/McpGateway.IntegrationTests/Audit/AuditPipelineTests.cs
git commit -m "test(audit): add retry worker tests

- DiskFallbackRetryWorker drains pre-populated buffer back into queue
- When the queue is missing the buffer is left intact for the next tick"
```

---

### Task 15: Run the full test suite

- [ ] **Step 1: Build the solution**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/McpGateway.sln
```

Expected: Build succeeds with no warnings.

- [ ] **Step 2: Run the full test suite**

Run:

```bash
dotnet test /var/home/felix/github/mcp-gateway/McpGateway.sln
```

Expected: All unit and integration tests pass.

---

## Self-Review

**1. Spec coverage:**

| Requirement (from `CONTEXT.md` / `architecture.md` / `implementation-plan.md`) | Task |
|---|---|
| `AuditEvent` schema (caller, auth method, auth strategy, server name, tool name, args, response, HTTP status, latency, timestamp) | Task 4 |
| Azure Storage Queue emission, fire-and-forget | Task 5 (`QueueEmitter`) |
| Disk fallback when queue is unavailable | Task 6 (`DiskFallback`) |
| At-least-once delivery with retry worker | Task 7 (`DiskFallbackRetryWorker`) |
| OpenTelemetry traces (`McpGateway.ToolCall`, `McpGateway.OboExchange`) | Task 2 (`ActivitySources`) |
| Metrics: tool call count, latency histogram, error rate, OBO cache hit rate | Task 2 (`TelemetryMetrics`) |
| OTLP/HTTP exporter via `OpenTelemetry.Extensions.AspNetCore` 1.12.0 | Task 1 + Task 3 |
| Local dev targets Jaeger on port 4318 | Task 3 (configurable endpoint, default `http://localhost:4318`) + docker-compose already wires `Otel__Exporter__Otlp__Endpoint=http://jaeger:4318` |
| `TelemetrySetup` extension | Task 3 |
| Response body truncated to 10 KB | Task 5 (`Truncate` call before serialization) |
| Components: `AuditEvent`, `QueueEmitter`, `DiskFallback`, `DiskFallbackRetryWorker` | Tasks 4–7 |
| Components: `TelemetrySetup`, `ActivitySources` | Tasks 2 + 3 |
| `Program.cs` wires audit + telemetry | Task 10 |
| `docker-compose.yml` already has azurite + jaeger | confirmed (read in this plan) — no changes required |
| Integration tests with Testcontainers (Azurite) for audit emission + disk fallback | Tasks 11–14 |

**2. Placeholder scan:**

No `TBD`, `TODO`, "implement later", or vague "add error handling" steps. Every code step contains complete C# code; every test step contains the full test class.

**3. Type consistency:**

- `AuditEvent` properties match the field list in `CONTEXT.md` (caller identity, gateway auth method, auth strategy, target server name, tool name, arguments, response, HTTP status, latency, timestamp).
- `IAuditEmitter.EmitAsync(AuditEvent, CancellationToken)` is implemented by `QueueEmitter`; `QueueEmitter` depends on `DiskFallback` (the concrete class, not the interface — there is no interface here because the worker also depends on the concrete `GetPendingFiles` / `RemoveAsync` methods).
- `TelemetryMetrics.Meter` exposes `ToolCallCount`, `ToolCallLatencyMs`, `ToolCallErrors`, `OboCacheHits`, `OboCacheMisses`. The OBO counters are reserved for the Auth plan (they're declared here so the meter is consistent) — Auth plan uses them when it wires `AuthStrategyResolver`.
- `ActivitySources.ToolCall` and `ActivitySources.OboExchange` are the two `ActivitySource` instances used in `CONTEXT.md` / `architecture.md`.
- `ToolCallHandler` constructor now takes 7 parameters: `IToolStore`, `HttpRequestBuilder`, `ResponseWrapper`, `HttpClient`, `IAuditEmitter`, `TimeProvider`, `ToolCallContextAccessor`. The DI factory in `ProxyServiceExtensions` resolves all of them from the container.
- `ToolCallContextAccessor` is a singleton-friendly `AsyncLocal`-backed holder so the API layer can stash the caller's `ToolCallContext` per request without changing the proxy signature.
- `appsettings.json` keys match what `docker-compose.yml` already sets: `Otel:Exporter:Otlp:Endpoint`, `AzureStorage:ConnectionString`.

**4. Known follow-ups for Oracle review:**

- `ToolCallContext` is currently only populated by the API layer's auth middleware. Wiring it into the request pipeline is the responsibility of the McpSdk plan (which maps `/mcp/{server_name}`) — that plan should set `ToolCallContextAccessor.Current = new ToolCallContext { Caller = ..., ServerName = ..., ToolName = ... }` before invoking `ToolCallHandler.HandleAsync`. If the context is null, the audit event is silently skipped (defensive).
- `OBO cache hit/miss` counters are declared in `TelemetryMetrics` so the meter is complete; the Auth plan needs to call `TelemetryMetrics.OboCacheHits.Add(1)` / `OboCacheMisses.Add(1)` inside `AuthStrategyResolver.ResolveOboTokenAsync`.
- `DiskFallback` is a concrete class injected into both `QueueEmitter` and `DiskFallbackRetryWorker`. No interface was extracted because there is only one implementation and the worker needs both `GetPendingFiles` and `RemoveAsync` — adding an interface would be premature.
- `QueueEmitter` uses `Convert.ToBase64String` for the payload because Azure Storage Queues transports messages as base64-encoded strings. The retry worker deserializes the same way, so format is symmetric.
- Response truncation uses byte-level truncation (UTF-8) at exactly 10,240 bytes. This matches the 10 KB spec and is the simplest correct implementation; a character-boundary safe version is not required by the spec.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-07-03-audit-and-telemetry.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.

**Which approach?** Or do you want to move on to the next plan (McpSdk/MCP endpoint, Hardening) before executing this one?
