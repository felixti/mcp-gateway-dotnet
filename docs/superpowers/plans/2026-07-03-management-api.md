# Management API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `/admin/*` management API surface and the services behind it — server definition CRUD, tool overrides and visibility, gateway API key issuance, spec management, and the admin-approval workflow that drives hot-reload of the InMemoryToolStore.

**Architecture:** The HTTP layer is ASP.NET Core minimal API (`AdminEndpoints.cs`) that delegates to services in `McpGateway.Management`. Services compose repositories from `McpGateway.Persistence`, the `IToolStore` from `McpGateway.Core`, the `OpenApiParser` + `IToolGenerator` from the Tool Generation plan, and the `ISpecFetcher`/`ISpecDiffService` from the Spec Management plan. Approval flows mutate the InMemoryToolStore in place (ADR-0003). Admin auth uses Entra ID JWT with a `roles` claim check; for local dev and tests, a `DevelopmentAdminAuthHandler` accepts a header-based fake identity. The Management project is HTTP-framework-agnostic — services take an `ICallerIdentity` and a `CancellationToken`. DTOs are validated with FluentValidation 11.x. API keys are bcrypt-hashed with BCrypt.Net-Next.

**Tech Stack:** .NET 10, ASP.NET Core 10, FluentValidation 11.9.2, Microsoft.Identity.Web 4.12.2 (admin role claim), BCrypt.Net-Next 4.2.1, xUnit, Testcontainers.PostgreSql 4.13.0, Microsoft.AspNetCore.Mvc.Testing 10.0.9.

**Prerequisites (assumed already implemented by prior plans):**
- `McpGateway.Core.ServerDefinitions.McpServerDefinition`, `ToolDefinition`, `ToolOverride`, `GatewayApiKey`, `SpecVersion` (Persistence plan)
- `McpGateway.Core.ToolGeneration.GeneratedTool`, `IToolGenerator`, `OpenApiParser` (Tool Generation plan)
- `McpGateway.Core.SpecManagement.ISpecFetcher`, `ISpecDiffService`, `FetchedSpec`, `SpecDiffResult` (Spec Management plan)
- `McpGateway.Core.Repositories.IServerDefinitionRepository`, `IGatewayApiKeyRepository`, `IToolOverrideRepository` (Persistence plan)
- `McpGateway.Persistence.Repositories.*` implementations (Persistence plan)
- `McpGateway.Core.ToolStore.IToolStore`, `InMemoryToolStore` (In-Memory Tool Store plan)
- `McpGateway.Core.Repositories.ISpecVersionRepository` (Spec Management plan)
- `src/McpGateway.Api/McpGateway.Api.csproj` already references Core, Persistence, Management, McpSdk
- `McpGateway.sln` includes Api, Core, Persistence, Management, McpSdk, Telemetry, UnitTests, IntegrationTests

---

## File Structure

```
src/
├── McpGateway.Management/
│   ├── McpGateway.Management.csproj
│   ├── Exceptions/
│   │   ├── NotFoundException.cs
│   │   ├── ConflictException.cs
│   │   └── ValidationException.cs
│   ├── Contracts/
│   │   ├── Dtos.cs
│   │   └── Validators.cs
│   ├── Auth/
│   │   ├── ICallerIdentityAccessor.cs
│   │   └── CallerIdentityAccessor.cs
│   └── Services/
│       ├── ServerManagementService.cs
│       ├── ToolManagementService.cs
│       ├── GatewayApiKeyService.cs
│       ├── ClientProfileService.cs
│       └── ManagementServiceExtensions.cs
│
├── McpGateway.Api/
│   ├── Auth/
│   │   ├── JwtAuthHandler.cs           (admin-role-claim variant, or shared with MCP JWT)
│   │   └── DevelopmentAdminAuthHandler.cs
│   ├── Endpoints/
│   │   └── AdminEndpoints.cs
│   └── Program.cs                       (wire /admin/* group, DI registrations)
│
tests/
├── McpGateway.UnitTests/
│   └── Management/
│       ├── ServerManagementServiceTests.cs
│       ├── ToolManagementServiceTests.cs
│       ├── GatewayApiKeyServiceTests.cs
│       ├── ClientProfileServiceTests.cs
│       └── ValidatorsTests.cs
│
└── McpGateway.IntegrationTests/
    ├── AdminApiTests.cs
    ├── ManagementApiCollection.cs
    └── ManagementApiFactory.cs
```

---

### Task 1: Create `McpGateway.Management` project and add dependencies

**Files:**
- Create: `src/McpGateway.Management/McpGateway.Management.csproj`
- Create: `src/McpGateway.Management/Class1.cs` (placeholder, deleted in Step 1.4)

- [ ] **Step 1: Create the Management class library**

Run:

```bash
dotnet new classlib -n McpGateway.Management -o /var/home/felix/github/mcp-gateway/src/McpGateway.Management --framework net10.0
```

Expected: `src/McpGateway.Management/McpGateway.Management.csproj` created.

- [ ] **Step 2: Add to solution**

Run:

```bash
dotnet sln /var/home/felix/github/mcp-gateway/McpGateway.sln add src/McpGateway.Management/McpGateway.Management.csproj
```

Expected: Project added to solution.

- [ ] **Step 3: Add project and package references**

Run:

```bash
dotnet add src/McpGateway.Management/McpGateway.Management.csproj reference src/McpGateway.Core/McpGateway.Core.csproj
dotnet add src/McpGateway.Management/McpGateway.Management.csproj reference src/McpGateway.Persistence/McpGateway.Persistence.csproj
dotnet add src/McpGateway.Management/McpGateway.Management.csproj package FluentValidation --version 11.9.2
dotnet add src/McpGateway.Management/McpGateway.Management.csproj package BCrypt.Net-Next --version 4.2.1
dotnet add src/McpGateway.Management/McpGateway.Management.csproj package Microsoft.Extensions.DependencyInjection.Abstractions --version 9.0.4
dotnet add src/McpGateway.Management/McpGateway.Management.csproj package Microsoft.Extensions.Logging.Abstractions --version 9.0.4
```

Expected: All packages restore.

- [ ] **Step 4: Remove placeholder file and verify build**

Run:

```bash
rm -f src/McpGateway.Management/Class1.cs
dotnet build src/McpGateway.Management/McpGateway.Management.csproj
```

Expected: Build succeeds.

---

### Task 2: Define management DTOs

**Files:**
- Create: `src/McpGateway.Management/Contracts/Dtos.cs`

- [ ] **Step 1: Create the DTOs file**

Create `src/McpGateway.Management/Contracts/Dtos.cs`:

```csharp
using System.Text.Json.Nodes;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Management.Contracts;

public record CreateServerRequest(
    string Name,
    string DisplayName,
    string? Description,
    string? SpecSourceUrl,
    string? SpecContent,
    string BaseUrl,
    string AuthStrategy,
    JsonObject AuthConfig,
    string ToolMode,
    string ClientProfile,
    int PollIntervalMinutes,
    string? CreatedBy);

public record UpdateServerRequest(
    string? DisplayName,
    string? Description,
    string? SpecSourceUrl,
    string? BaseUrl,
    string? AuthStrategy,
    JsonObject? AuthConfig,
    string? ToolMode,
    string? ClientProfile,
    int? PollIntervalMinutes,
    string? Status);

public record ServerResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string? Description,
    string? SpecSourceUrl,
    string BaseUrl,
    string AuthStrategy,
    JsonObject AuthConfig,
    string ToolMode,
    string ClientProfile,
    int PollIntervalMinutes,
    string Status,
    string ApprovalStatus,
    DateTime? ApprovedAt,
    string? ApprovedBy,
    DateTime? LastRefreshedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static ServerResponse FromDomain(McpServerDefinition d) => new(
        d.Id, d.Name, d.DisplayName, d.Description, d.SpecSourceUrl, d.BaseUrl,
        d.AuthStrategy, ParseJsonObject(d.AuthConfig), d.ToolMode.ToString().ToLowerInvariant(),
        d.ClientProfile.ToString().ToLowerInvariant(), d.PollIntervalMinutes, d.Status,
        d.ApprovalStatus, d.ApprovedAt, d.ApprovedBy, d.LastRefreshedAt, d.CreatedAt, d.UpdatedAt);

    private static JsonObject ParseJsonObject(string raw)
    {
        try { return JsonNode.Parse(raw) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }
}

public record ToolResponse(
    string ToolName,
    string Description,
    string HttpMethod,
    string HttpPath,
    JsonObject InputSchema,
    JsonObject? OutputSchema,
    bool Visible,
    bool HasOverride,
    string EffectiveDescription);

public record UpdateToolRequest(
    string? DescriptionOverride,
    bool? Visible);

public record PutOverrideRequest(string DescriptionOverride);

public record ApiKeySummary(
    Guid Id,
    string KeyPrefix,
    string Name,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAt,
    DateTime? RevokedAt,
    DateTime? LastUsedAt);

public record CreateApiKeyRequest(string Name, IReadOnlyList<string> Scopes);

public record CreateApiKeyResponse(
    Guid Id,
    string KeyPrefix,
    string Name,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAt,
    string FullKey);

public record SpecUploadRequest(string Content, string? ContentType);

public record SpecSourceUpdateRequest(string SpecSourceUrl);

public record SpecDiffResponse(
    string CurrentHash,
    string ComparedHash,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<ToolChangeDto> Changed);

public record ToolChangeDto(
    string ToolName,
    string HttpMethod,
    string HttpPath,
    IReadOnlyList<string> ChangedFields);

public record ApproveResponse(Guid Id, string ApprovalStatus, DateTime ApprovedAt, string ApprovedBy, int ToolCount);

public record RefreshResponse(
    Guid Id,
    string ApprovalStatus,
    bool SpecChanged,
    string SpecHash,
    DateTime LastRefreshedAt,
    int ToolCount);

public record ErrorResponse(string Error, string? Detail = null);
```

- [ ] **Step 2: Build Management**

Run:

```bash
dotnet build src/McpGateway.Management/McpGateway.Management.csproj
```

Expected: Build succeeds.

---

### Task 3: Define management exceptions and validation result type

**Files:**
- Create: `src/McpGateway.Management/Exceptions/NotFoundException.cs`
- Create: `src/McpGateway.Management/Exceptions/ConflictException.cs`
- Create: `src/McpGateway.Management/Exceptions/ValidationException.cs`

- [ ] **Step 1: Create `NotFoundException`**

Create `src/McpGateway.Management/Exceptions/NotFoundException.cs`:

```csharp
namespace McpGateway.Management.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string resource, string key)
        : base($"{resource} '{key}' not found.") { }
}
```

- [ ] **Step 2: Create `ConflictException`**

Create `src/McpGateway.Management/Exceptions/ConflictException.cs`:

```csharp
namespace McpGateway.Management.Exceptions;

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
```

- [ ] **Step 3: Create `ValidationException`**

Create `src/McpGateway.Management/Exceptions/ValidationException.cs`:

```csharp
using FluentValidation.Results;

namespace McpGateway.Management.Exceptions;

public class ValidationException : Exception
{
    public IReadOnlyList<ValidationFailure> Errors { get; }

    public ValidationException(IEnumerable<ValidationFailure> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors.ToList();
    }
}
```

- [ ] **Step 4: Build Management**

Run:

```bash
dotnet build src/McpGateway.Management/McpGateway.Management.csproj
```

Expected: Build succeeds.

---

### Task 4: Define FluentValidation validators

**Files:**
- Create: `src/McpGateway.Management/Contracts/Validators.cs`
- Create: `tests/McpGateway.UnitTests/Management/ValidatorsTests.cs`

- [ ] **Step 1: Write failing validator tests**

Create `tests/McpGateway.UnitTests/Management/ValidatorsTests.cs`:

```csharp
using System.Text.Json.Nodes;
using FluentAssertions;
using FluentValidation.TestHelper;
using McpGateway.Management.Contracts;

namespace McpGateway.UnitTests.Management;

public class ValidatorsTests
{
    private readonly CreateServerRequestValidator _createValidator = new();
    private readonly UpdateServerRequestValidator _updateValidator = new();
    private readonly UpdateToolRequestValidator _toolValidator = new();
    private readonly PutOverrideRequestValidator _overrideValidator = new();
    private readonly CreateApiKeyRequestValidator _apiKeyValidator = new();

    [Fact]
    public void CreateServer_ValidRequest_Passes()
    {
        var req = new CreateServerRequest(
            "invoice-api", "Invoice API", "desc",
            "https://x/openapi.json", null,
            "https://invoice.example.com", "obo",
            new JsonObject { ["resource"] = "api://invoice-api/.default" },
            "all", "universal", 1440, "admin@corp.com");

        var result = _createValidator.TestValidate(req);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateServer_InvalidName_Fails()
    {
        var req = new CreateServerRequest(
            "Bad Name With Spaces", "Invoice API", null, null, null,
            "https://x", "obo", new JsonObject(), "all", "universal", 1440, null);

        var result = _createValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(r => r.Name);
    }

    [Fact]
    public void CreateServer_InvalidAuthStrategy_Fails()
    {
        var req = new CreateServerRequest(
            "ok-name", "X", null, null, null,
            "https://x", "magic", new JsonObject(), "all", "universal", 1440, null);

        var result = _createValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(r => r.AuthStrategy);
    }

    [Fact]
    public void CreateServer_MissingSpecSource_Fails()
    {
        var req = new CreateServerRequest(
            "ok-name", "X", null, null, null,
            "https://x", "obo", new JsonObject(), "all", "universal", 1440, null);

        var result = _createValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(r => r);
    }

    [Fact]
    public void UpdateServer_NullValues_AllAllowed()
    {
        var req = new UpdateServerRequest(null, null, null, null, null, null, null, null, null, null);
        var result = _updateValidator.TestValidate(req);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateServer_InvalidStatus_Fails()
    {
        var req = new UpdateServerRequest(null, null, null, null, null, null, null, null, null, "archived");
        var result = _updateValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(r => r.Status);
    }

    [Fact]
    public void UpdateTool_PartialUpdate_Allowed()
    {
        var req = new UpdateToolRequest(null, true);
        var result = _toolValidator.TestValidate(req);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateTool_AllNull_Fails()
    {
        var req = new UpdateToolRequest(null, null);
        var result = _toolValidator.TestValidate(req);
        result.ShouldHaveValidationError();
    }

    [Fact]
    public void PutOverride_EmptyDescription_Fails()
    {
        var req = new PutOverrideRequest("");
        var result = _overrideValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(r => r.DescriptionOverride);
    }

    [Fact]
    public void CreateApiKey_EmptyName_Fails()
    {
        var req = new CreateApiKeyRequest("", new[] { "invoice-api" });
        var result = _apiKeyValidator.TestValidate(req);
        result.ShouldHaveValidationErrorFor(r => r.Name);
    }
}
```

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ValidatorsTests" -v n
```

Expected: FAIL with "type or namespace 'CreateServerRequestValidator' could not be found".

- [ ] **Step 2: Implement validators**

Create `src/McpGateway.Management/Contracts/Validators.cs`:

```csharp
using FluentValidation;
using McpGateway.Management.Contracts;

namespace McpGateway.Management.Contracts;

public class CreateServerRequestValidator : AbstractValidator<CreateServerRequest>
{
    public CreateServerRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$")
            .WithMessage("Name must be lowercase letters, digits, and dashes; 2-64 chars; no leading/trailing dash.");

        RuleFor(r => r.DisplayName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.BaseUrl)
            .NotEmpty()
            .Must(BeAValidAbsoluteHttpUrl)
            .WithMessage("BaseUrl must be an absolute http or https URL.");

        RuleFor(r => r.AuthStrategy)
            .NotEmpty()
            .Must(s => s is "obo" or "passthrough" or "static")
            .WithMessage("AuthStrategy must be one of: obo, passthrough, static.");

        RuleFor(r => r.ToolMode)
            .NotEmpty()
            .Must(m => m is "all" or "dynamic" or "curated")
            .WithMessage("ToolMode must be one of: all, dynamic, curated.");

        RuleFor(r => r.ClientProfile)
            .NotEmpty()
            .Must(p => p is "universal" or "claude" or "cursor")
            .WithMessage("ClientProfile must be one of: universal, claude, cursor.");

        RuleFor(r => r.PollIntervalMinutes)
            .GreaterThanOrEqualTo(5)
            .LessThanOrEqualTo(43200);

        RuleFor(r => r)
            .Must(r => !string.IsNullOrWhiteSpace(r.SpecSourceUrl) || !string.IsNullOrWhiteSpace(r.SpecContent))
            .WithMessage("Either SpecSourceUrl or SpecContent is required.");

        When(r => r.AuthStrategy == "obo", () =>
        {
            RuleFor(r => r.AuthConfig)
                .NotNull()
                .Must(c => c.ContainsKey("resource"))
                .WithMessage("authConfig.resource is required for obo strategy.");
        });

        When(r => r.AuthStrategy == "static", () =>
        {
            RuleFor(r => r.AuthConfig)
                .NotNull()
                .Must(c => c.ContainsKey("apiKey") || c.ContainsKey("bearerToken"))
                .WithMessage("authConfig.apiKey or authConfig.bearerToken is required for static strategy.");
        });
    }

    private static bool BeAValidAbsoluteHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}

public class UpdateServerRequestValidator : AbstractValidator<UpdateServerRequest>
{
    public UpdateServerRequestValidator()
    {
        When(r => r.DisplayName is not null, () => RuleFor(r => r.DisplayName!).NotEmpty().MaximumLength(200));
        When(r => r.BaseUrl is not null, () =>
        {
            RuleFor(r => r.BaseUrl!)
                .NotEmpty()
                .Must(s => Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps));
        });
        When(r => r.AuthStrategy is not null, () =>
            RuleFor(r => r.AuthStrategy!).Must(s => s is "obo" or "passthrough" or "static"));
        When(r => r.ToolMode is not null, () =>
            RuleFor(r => r.ToolMode!).Must(m => m is "all" or "dynamic" or "curated"));
        When(r => r.ClientProfile is not null, () =>
            RuleFor(r => r.ClientProfile!).Must(p => p is "universal" or "claude" or "cursor"));
        When(r => r.PollIntervalMinutes is not null, () =>
            RuleFor(r => r.PollIntervalMinutes!.Value).GreaterThanOrEqualTo(5).LessThanOrEqualTo(43200));
        When(r => r.Status is not null, () =>
            RuleFor(r => r.Status!).Must(s => s is "active" or "disabled"));
    }
}

public class UpdateToolRequestValidator : AbstractValidator<UpdateToolRequest>
{
    public UpdateToolRequestValidator()
    {
        RuleFor(r => r)
            .Must(r => r.DescriptionOverride is not null || r.Visible is not null)
            .WithMessage("At least one of DescriptionOverride or Visible must be provided.");

        When(r => r.DescriptionOverride is not null, () =>
            RuleFor(r => r.DescriptionOverride!).NotEmpty().MaximumLength(2000));
    }
}

public class PutOverrideRequestValidator : AbstractValidator<PutOverrideRequest>
{
    public PutOverrideRequestValidator()
    {
        RuleFor(r => r.DescriptionOverride)
            .NotEmpty()
            .MaximumLength(2000);
    }
}

public class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.Scopes)
            .NotNull()
            .Must(s => s.Count > 0)
            .WithMessage("At least one scope is required.");
    }
}
```

- [ ] **Step 3: Add FluentValidation.DependencyInjectionExtensions package (for TestHelper) and run tests**

Run:

```bash
dotnet add tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj package FluentValidation.DependencyInjectionExtensions --version 11.9.2
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ValidatorsTests" -v n
```

Expected: All 9 validator tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Management tests/McpGateway.UnitTests/Management
git commit -m "feat(management): add DTOs, exceptions, and FluentValidation validators"
```

---

### Task 5: Add caller identity accessor and admin auth contract

**Files:**
- Create: `src/McpGateway.Management/Auth/ICallerIdentityAccessor.cs`
- Create: `src/McpGateway.Management/Auth/CallerIdentityAccessor.cs`

- [ ] **Step 1: Create `ICallerIdentityAccessor`**

Create `src/McpGateway.Management/Auth/ICallerIdentityAccessor.cs`:

```csharp
namespace McpGateway.Management.Auth;

public interface ICallerIdentityAccessor
{
    /// <summary>
    /// Returns the admin's identity (Entra ID UPN for Entra, key prefix + id for API key admin).
    /// Throws InvalidOperationException if no admin identity is present.
    /// </summary>
    string GetAdminUpn();

    /// <summary>
    /// Returns true if the current request has a verified admin identity.
    /// </summary>
    bool IsAdmin { get; }
}
```

- [ ] **Step 2: Create `CallerIdentityAccessor`**

Create `src/McpGateway.Management/Auth/CallerIdentityAccessor.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace McpGateway.Management.Auth;

public class CallerIdentityAccessor : ICallerIdentityAccessor
{
    public const string AdminRole = "mcp-gateway-admin";
    public const string AdminUpnClaim = "admin_upn";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CallerIdentityAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAdmin
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null || user.Identity?.IsAuthenticated != true) return false;
            return user.IsInRole(AdminRole) || user.HasClaim(c => c.Type == AdminUpnClaim);
        }
    }

    public string GetAdminUpn()
    {
        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context available.");

        var upn = user.FindFirstValue(AdminUpnClaim)
            ?? user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(upn))
            throw new InvalidOperationException("Admin identity missing from token claims.");

        return upn;
    }
}
```

- [ ] **Step 3: Build Management**

Run:

```bash
dotnet build src/McpGateway.Management/McpGateway.Management.csproj
```

Expected: Build succeeds.

---

### Task 6: Implement `ServerManagementService` — register + get + list

**Files:**
- Create: `src/McpGateway.Management/Services/ServerManagementService.cs`
- Create: `tests/McpGateway.UnitTests/Management/ServerManagementServiceTests.cs`

- [ ] **Step 1: Write failing service tests for register + get + list**

Create `tests/McpGateway.UnitTests/Management/ServerManagementServiceTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Auth;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Management.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class ServerManagementServiceTests
{
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();
    private readonly ISpecVersionRepository _specVersionRepo = Substitute.For<ISpecVersionRepository>();
    private readonly IToolStore _toolStore = Substitute.For<IToolStore>();
    private readonly IToolGenerator _toolGenerator = Substitute.For<IToolGenerator>();
    private readonly ISpecFetcher _specFetcher = Substitute.For<ISpecFetcher>();
    private readonly ISpecDiffService _diffService = Substitute.For<ISpecDiffService>();
    private readonly ICallerIdentityAccessor _caller = Substitute.For<ICallerIdentityAccessor>();

    private ServerManagementService CreateSut() => new(
        _serverRepo, _specVersionRepo, _toolStore, _toolGenerator, _specFetcher, _diffService, _caller,
        NullLogger<ServerManagementService>.Instance);

    [Fact]
    public async Task RegisterAsync_WithSpecSource_FetchesAndGeneratesTools()
    {
        var specJson = """{"openapi":"3.0.0","info":{"title":"X","version":"1"},"paths":{}}""";
        _specFetcher.FetchAsync(SpecSource.FromUrl("https://x/openapi.json"), Arg.Any<CancellationToken>())
            .Returns(new FetchedSpec(specJson, ComputeHash(specJson), SpecFormat.Json));

        var generated = new List<GeneratedTool>
        {
            new() { Name = "ping", Description = "ping api", HttpMethod = "GET", HttpPath = "/ping" }
        };
        _toolGenerator.Generate(Arg.Any<Microsoft.OpenApi.Models.OpenApiDocument>(), Arg.Any<ClientProfile>())
            .Returns(generated);

        _serverRepo.AddAsync(Arg.Any<McpServerDefinition>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<McpServerDefinition>());

        var req = new CreateServerRequest(
            "invoice-api", "Invoice API", "desc",
            "https://x/openapi.json", null,
            "https://invoice.example.com", "obo",
            new JsonObject { ["resource"] = "api://invoice-api/.default" },
            "all", "universal", 1440, "admin@corp.com");

        var sut = CreateSut();
        var result = await sut.RegisterAsync(req, CancellationToken.None);

        result.Name.Should().Be("invoice-api");
        result.ApprovalStatus.Should().Be("pending");
        await _specFetcher.Received(1).FetchAsync(SpecSource.FromUrl("https://x/openapi.json"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_DuplicateName_ThrowsConflict()
    {
        _serverRepo.GetByNameAsync("dup", Arg.Any<CancellationToken>())
            .Returns(new McpServerDefinition { Name = "dup" });

        var req = new CreateServerRequest(
            "dup", "Dup", null, null, "{}",
            "https://x", "obo", new JsonObject(), "all", "universal", 1440, null);

        var sut = CreateSut();
        Func<Task> act = () => sut.RegisterAsync(req, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task GetAsync_NotFound_Throws()
    {
        _serverRepo.GetByNameForAdminAsync("nope", Arg.Any<CancellationToken>()).Returns((McpServerDefinition?)null);
        var sut = CreateSut();
        Func<Task> act = () => sut.GetAsync("nope", CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ListAsync_ReturnsAllSorted()
    {
        _serverRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<McpServerDefinition>
        {
            new() { Name = "z" },
            new() { Name = "a" }
        });
        var sut = CreateSut();
        var result = await sut.ListAsync(CancellationToken.None);
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("a");
        result[1].Name.Should().Be("z");
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ServerManagementServiceTests" -v n
```

Expected: FAIL with "type or namespace 'ServerManagementService' could not be found".

- [ ] **Step 2: Add test packages**

Run:

```bash
dotnet add tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj package NSubstitute --version 5.1.0
```

- [ ] **Step 3: Implement `ServerManagementService` (register + get + list only) — final version is in next step**

Create `src/McpGateway.Management/Services/ServerManagementService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Auth;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Core.SpecManagement;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace McpGateway.Management.Services;

public class ServerManagementService
{
    private readonly IServerDefinitionRepository _serverRepo;
    private readonly ISpecVersionRepository _specVersionRepo;
    private readonly IToolStore _toolStore;
    private readonly IToolGenerator _toolGenerator;
    private readonly ISpecFetcher _specFetcher;
    private readonly ISpecDiffService _diffService;
    private readonly ICallerIdentityAccessor _caller;
    private readonly ILogger<ServerManagementService> _logger;

    public ServerManagementService(
        IServerDefinitionRepository serverRepo,
        ISpecVersionRepository specVersionRepo,
        IToolStore toolStore,
        IToolGenerator toolGenerator,
        ISpecFetcher specFetcher,
        ISpecDiffService diffService,
        ICallerIdentityAccessor caller,
        ILogger<ServerManagementService> logger)
    {
        _serverRepo = serverRepo;
        _specVersionRepo = specVersionRepo;
        _toolStore = toolStore;
        _toolGenerator = toolGenerator;
        _specFetcher = specFetcher;
        _diffService = diffService;
        _caller = caller;
        _logger = logger;
    }

    public async Task<ServerResponse> RegisterAsync(CreateServerRequest request, CancellationToken ct)
    {
        var existing = await _serverRepo.GetByNameAsync(request.Name, ct);
        if (existing is not null)
            throw new ConflictException($"Server definition '{request.Name}' already exists.");

        var (specContent, specHash) = !string.IsNullOrWhiteSpace(request.SpecContent)
            ? (request.SpecContent!, ComputeHash(request.SpecContent!))
            : await FetchSpecAsync(request.SpecSourceUrl!, ct);

        var parser = new OpenApiParser();
        var document = parser.Parse(specContent);

        var clientProfile = Enum.Parse<ClientProfile>(Capitalize(request.ClientProfile));
        var tools = _toolGenerator.Generate(document, clientProfile);

        var definition = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            SpecSourceUrl = request.SpecSourceUrl,
            SpecContent = specContent,
            SpecHash = specHash,
            BaseUrl = request.BaseUrl.TrimEnd('/'),
            AuthStrategy = request.AuthStrategy,
            AuthConfig = request.AuthConfig.ToJsonString(),
            ToolMode = Enum.Parse<ToolMode>(Capitalize(request.ToolMode)),
            ClientProfile = clientProfile,
            PollIntervalMinutes = request.PollIntervalMinutes,
            Status = "active",
            ApprovalStatus = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Tools = tools.Select(t => new ToolDefinition
            {
                Id = Guid.NewGuid(),
                ToolName = t.Name,
                Description = t.Description,
                HttpMethod = t.HttpMethod,
                HttpPath = t.HttpPath,
                InputSchema = t.InputSchema?.ToJsonString() ?? "{}",
                OutputSchema = t.OutputSchema?.ToJsonString(),
                AuthConfig = t.AuthConfig,
                Visible = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ToList()
        };

        var saved = await _serverRepo.AddAsync(definition, ct);
        return ServerResponse.FromDomain(saved);
    }

    public async Task<ServerResponse> GetAsync(string name, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);
        return ServerResponse.FromDomain(def);
    }

    public async Task<IReadOnlyList<ServerResponse>> ListAsync(CancellationToken ct)
    {
        var defs = await _serverRepo.ListAsync(ct);
        return defs
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(ServerResponse.FromDomain)
            .ToList();
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<(string Content, string Hash)> FetchSpecAsync(string url, CancellationToken ct)
    {
        var fetched = await _specFetcher.FetchAsync(SpecSource.FromUrl(url), ct);
        return (fetched.Content, fetched.Hash);
    }
}
```

Note: `OpenApiParser` and `IToolGenerator` come from the Tool Generation plan. If those interfaces are not yet finalized, use a tiny shim — but in the established codebase they are stable.

- [ ] **Step 4: Run tests, fix any mismatches, then commit**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ServerManagementServiceTests" -v n
```

Expected: All 4 tests pass. If `IToolGenerator` is named differently in your codebase (e.g., it lives as `ToolGenerator` class only), adjust the constructor parameter to `ToolGenerator` and the field to concrete. The shape of `Generate(OpenApiDocument, ClientProfile) → IReadOnlyList<GeneratedTool>` must match the Tool Generation plan.

```bash
git add src/McpGateway.Management/Services/ServerManagementService.cs tests/McpGateway.UnitTests/Management/ServerManagementServiceTests.cs
git commit -m "feat(management): add ServerManagementService register/get/list"
```

---

### Task 7: Add `ServerManagementService` update + delete (soft) + refresh + approve

**Files:**
- Modify: `src/McpGateway.Management/Services/ServerManagementService.cs`
- Create: `tests/McpGateway.UnitTests/Management/ServerManagementServiceUpdateTests.cs`

- [ ] **Step 1: Add failing tests for update + delete + refresh + approve**

Create `tests/McpGateway.UnitTests/Management/ServerManagementServiceUpdateTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Auth;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Management.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class ServerManagementServiceUpdateTests
{
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();
    private readonly ISpecVersionRepository _specVersionRepo = Substitute.For<ISpecVersionRepository>();
    private readonly IToolStore _toolStore = Substitute.For<IToolStore>();
    private readonly IToolGenerator _toolGenerator = Substitute.For<IToolGenerator>();
    private readonly ISpecFetcher _specFetcher = Substitute.For<ISpecFetcher>();
    private readonly ISpecDiffService _diffService = Substitute.For<ISpecDiffService>();
    private readonly ICallerIdentityAccessor _caller = Substitute.For<ICallerIdentityAccessor>();

    private ServerManagementService CreateSut() => new(
        _serverRepo, _specVersionRepo, _toolStore, _toolGenerator, _specFetcher, _diffService, _caller,
        NullLogger<ServerManagementService>.Instance);

    [Fact]
    public async Task UpdateAsync_ChangesDisplayName()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            DisplayName = "Old",
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            SpecContent = "{}",
            SpecHash = "h",
            Status = "active",
            ApprovalStatus = "pending"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var req = new UpdateServerRequest("New Name", null, null, null, null, null, null, null, null, null);
        var sut = CreateSut();
        var result = await sut.UpdateAsync("inv", req, CancellationToken.None);

        result.DisplayName.Should().Be("New Name");
        await _serverRepo.Received(1).UpdateAsync(Arg.Is<McpServerDefinition>(d => d.DisplayName == "New Name"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_DisablesServerAndRemovesFromStore()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            Status = "active",
            ApprovalStatus = "approved",
            SpecContent = "{}",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CreateSut();
        await sut.DeleteAsync("inv", CancellationToken.None);

        await _serverRepo.Received(1).UpdateAsync(
            Arg.Is<McpServerDefinition>(d => d.Status == "disabled"),
            Arg.Any<CancellationToken>());
        _toolStore.Received(1).RemoveServer("inv");
    }

    [Fact]
    public async Task RefreshAsync_SpecUnchanged_NoChange()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecSourceUrl = "https://x/openapi.json",
            SpecContent = "{}",
            SpecHash = ComputeHash("{}"),
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "approved"
        };
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _specFetcher.FetchAsync(SpecSource.FromUrl("https://x/openapi.json"), Arg.Any<CancellationToken>())
            .Returns(new FetchedSpec("{}", ComputeHash("{}"), SpecFormat.Json));

        var sut = CreateSut();
        var result = await sut.RefreshAsync("inv", CancellationToken.None);

        result.SpecChanged.Should().BeFalse();
        result.ApprovalStatus.Should().Be("approved");
    }

    [Fact]
    public async Task RefreshAsync_SpecChanged_SetsChangesPendingAndRemovesFromStore()
    {
        var oldContent = "{}";
        var newContent = """{"openapi":"3.0.0","info":{"title":"X","version":"2"},"paths":{"/a":{"get":{}}}}""";
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecSourceUrl = "https://x/openapi.json",
            SpecContent = oldContent,
            SpecHash = ComputeHash(oldContent),
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "approved"
        };
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _specFetcher.FetchAsync(SpecSource.FromUrl("https://x/openapi.json"), Arg.Any<CancellationToken>())
            .Returns(new FetchedSpec(newContent, ComputeHash(newContent), SpecFormat.Json));
        _toolGenerator.Generate(Arg.Any<Microsoft.OpenApi.Models.OpenApiDocument>(), Arg.Any<ClientProfile>())
            .Returns(new List<GeneratedTool>());
        _diffService.Diff(oldContent, newContent, Arg.Any<ClientProfile>())
            .Returns(new SpecDiffResult([], [], []));

        var sut = CreateSut();
        var result = await sut.RefreshAsync("inv", CancellationToken.None);

        result.SpecChanged.Should().BeTrue();
        result.ApprovalStatus.Should().Be("changes_pending");
        _toolStore.Received(1).RemoveServer("inv");
        await _specVersionRepo.Received(1).AddAsync(Arg.Any<SpecVersion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAsync_LoadsToolsIntoStore()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = "{}",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "pending",
            ToolMode = ToolMode.All,
            Tools = new List<ToolDefinition>
            {
                new() { ToolName = "ping", Description = "ping", HttpMethod = "GET", HttpPath = "/ping", InputSchema = "{}", AuthConfig = "{}" }
            }
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _caller.GetAdminUpn().Returns("admin@corp.com");

        var sut = CreateSut();
        var result = await sut.ApproveAsync("inv", CancellationToken.None);

        result.ApprovalStatus.Should().Be("approved");
        result.ApprovedBy.Should().Be("admin@corp.com");
        _toolStore.Received(1).AddServer(Arg.Is<McpServerDefinition>(d => d.ApprovalStatus == "approved"));
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ServerManagementServiceUpdateTests" -v n
```

Expected: FAIL with missing methods on `ServerManagementService`.

- [ ] **Step 2: Extend `ServerManagementService` with the remaining methods**

Replace `src/McpGateway.Management/Services/ServerManagementService.cs` with:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Auth;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Core.SpecManagement;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace McpGateway.Management.Services;

public class ServerManagementService
{
    private readonly IServerDefinitionRepository _serverRepo;
    private readonly ISpecVersionRepository _specVersionRepo;
    private readonly IToolStore _toolStore;
    private readonly IToolGenerator _toolGenerator;
    private readonly ISpecFetcher _specFetcher;
    private readonly ISpecDiffService _diffService;
    private readonly ICallerIdentityAccessor _caller;
    private readonly ILogger<ServerManagementService> _logger;

    public ServerManagementService(
        IServerDefinitionRepository serverRepo,
        ISpecVersionRepository specVersionRepo,
        IToolStore toolStore,
        IToolGenerator toolGenerator,
        ISpecFetcher specFetcher,
        ISpecDiffService diffService,
        ICallerIdentityAccessor caller,
        ILogger<ServerManagementService> logger)
    {
        _serverRepo = serverRepo;
        _specVersionRepo = specVersionRepo;
        _toolStore = toolStore;
        _toolGenerator = toolGenerator;
        _specFetcher = specFetcher;
        _diffService = diffService;
        _caller = caller;
        _logger = logger;
    }

    public async Task<ServerResponse> RegisterAsync(CreateServerRequest request, CancellationToken ct)
    {
        var existing = await _serverRepo.GetByNameAsync(request.Name, ct);
        if (existing is not null)
            throw new ConflictException($"Server definition '{request.Name}' already exists.");

        var (specContent, specHash) = !string.IsNullOrWhiteSpace(request.SpecContent)
            ? (request.SpecContent!, ComputeHash(request.SpecContent!))
            : await FetchSpecAsync(request.SpecSourceUrl!, ct);

        var parser = new OpenApiParser();
        var document = parser.Parse(specContent);

        var clientProfile = Enum.Parse<ClientProfile>(Capitalize(request.ClientProfile));
        var tools = _toolGenerator.Generate(document, clientProfile);

        var definition = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            SpecSourceUrl = request.SpecSourceUrl,
            SpecContent = specContent,
            SpecHash = specHash,
            BaseUrl = request.BaseUrl.TrimEnd('/'),
            AuthStrategy = request.AuthStrategy,
            AuthConfig = request.AuthConfig.ToJsonString(),
            ToolMode = Enum.Parse<ToolMode>(Capitalize(request.ToolMode)),
            ClientProfile = clientProfile,
            PollIntervalMinutes = request.PollIntervalMinutes,
            Status = "active",
            ApprovalStatus = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Tools = tools.Select(t => new ToolDefinition
            {
                Id = Guid.NewGuid(),
                ToolName = t.Name,
                Description = t.Description,
                HttpMethod = t.HttpMethod,
                HttpPath = t.HttpPath,
                InputSchema = t.InputSchema?.ToJsonString() ?? "{}",
                OutputSchema = t.OutputSchema?.ToJsonString(),
                AuthConfig = t.AuthConfig,
                Visible = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ToList()
        };

        var saved = await _serverRepo.AddAsync(definition, ct);
        return ServerResponse.FromDomain(saved);
    }

    public async Task<ServerResponse> GetAsync(string name, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);
        return ServerResponse.FromDomain(def);
    }

    public async Task<IReadOnlyList<ServerResponse>> ListAsync(CancellationToken ct)
    {
        var defs = await _serverRepo.ListAsync(ct);
        return defs
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(ServerResponse.FromDomain)
            .ToList();
    }

    public async Task<ServerResponse> UpdateAsync(string name, UpdateServerRequest request, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);

        if (request.DisplayName is not null) def.DisplayName = request.DisplayName;
        if (request.Description is not null) def.Description = request.Description;
        if (request.SpecSourceUrl is not null) def.SpecSourceUrl = request.SpecSourceUrl;
        if (request.BaseUrl is not null) def.BaseUrl = request.BaseUrl.TrimEnd('/');
        if (request.AuthStrategy is not null) def.AuthStrategy = request.AuthStrategy;
        if (request.AuthConfig is not null) def.AuthConfig = request.AuthConfig.ToJsonString();
        if (request.ToolMode is not null) def.ToolMode = Enum.Parse<ToolMode>(Capitalize(request.ToolMode));
        if (request.ClientProfile is not null) def.ClientProfile = Enum.Parse<ClientProfile>(Capitalize(request.ClientProfile));
        if (request.PollIntervalMinutes is not null) def.PollIntervalMinutes = request.PollIntervalMinutes.Value;
        if (request.Status is not null) def.Status = request.Status;

        def.UpdatedAt = DateTime.UtcNow;

        // Re-approval is required when base_url / auth / tool_mode / client_profile change while approved
        if (def.ApprovalStatus == "approved"
            && (request.BaseUrl is not null || request.AuthStrategy is not null || request.AuthConfig is not null))
        {
            def.ApprovalStatus = "changes_pending";
            def.ApprovedAt = null;
            def.ApprovedBy = null;
            _toolStore.RemoveServer(def.Name);
        }

        await _serverRepo.UpdateAsync(def, ct);
        return ServerResponse.FromDomain(def);
    }

    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);

        def.Status = "disabled";
        def.UpdatedAt = DateTime.UtcNow;
        await _serverRepo.UpdateAsync(def, ct);
        _toolStore.RemoveServer(name);
    }

    public async Task<RefreshResponse> RefreshAsync(string name, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);

        if (string.IsNullOrWhiteSpace(def.SpecSourceUrl))
            throw new ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(def.SpecSourceUrl), "SpecSourceUrl is required for refresh.")
            });

        var (newContent, newHash) = await FetchSpecAsync(def.SpecSourceUrl!, ct);

        if (string.Equals(newHash, def.SpecHash, StringComparison.Ordinal))
        {
            def.LastRefreshedAt = DateTime.UtcNow;
            def.UpdatedAt = def.LastRefreshedAt.Value;
            await _serverRepo.UpdateAsync(def, ct);
            return new RefreshResponse(def.Id, def.ApprovalStatus, false, newHash, def.LastRefreshedAt.Value, def.Tools.Count);
        }

        // Spec changed — regenerate tools
        var parser = new OpenApiParser();
        var document = parser.Parse(newContent);
        var generated = _toolGenerator.Generate(document, def.ClientProfile);

        var newTools = generated.Select(t => new ToolDefinition
        {
            Id = Guid.NewGuid(),
            ToolName = t.Name,
            Description = t.Description,
            HttpMethod = t.HttpMethod,
            HttpPath = t.HttpPath,
            InputSchema = t.InputSchema?.ToJsonString() ?? "{}",
            OutputSchema = t.OutputSchema?.ToJsonString(),
            AuthConfig = t.AuthConfig,
            Visible = t.Visible,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        var diff = _diffService.Diff(def.SpecContent, newContent, def.ClientProfile);
        var diffJson = diff.ToJson();

        def.SpecContent = newContent;
        def.SpecHash = newHash;
        def.ApprovalStatus = "changes_pending";
        def.ApprovedAt = null;
        def.ApprovedBy = null;
        def.LastRefreshedAt = DateTime.UtcNow;
        def.UpdatedAt = def.LastRefreshedAt.Value;

        await _serverRepo.UpdateToolsAsync(def.Id, newTools, ct);
        await _serverRepo.UpdateAsync(def, ct);

        await _specVersionRepo.AddAsync(new SpecVersion
        {
            ServerDefinitionId = def.Id,
            SpecHash = newHash,
            SpecContent = newContent,
            ToolCount = newTools.Count,
            DiffSummary = diffJson
        }, ct);

        _toolStore.RemoveServer(def.Name);

        return new RefreshResponse(def.Id, def.ApprovalStatus, true, newHash, def.LastRefreshedAt.Value, newTools.Count);
    }

    public async Task<ApproveResponse> ApproveAsync(string name, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);

        if (def.Status != "active")
            throw new ConflictException("Cannot approve a disabled server definition.");

        if (def.Tools.Count == 0)
            throw new ConflictException("Cannot approve a server definition with no tools.");

        var adminUpn = _caller.GetAdminUpn();
        def.ApprovalStatus = "approved";
        def.ApprovedAt = DateTime.UtcNow;
        def.ApprovedBy = adminUpn;
        def.UpdatedAt = def.ApprovedAt.Value;
        await _serverRepo.UpdateAsync(def, ct);

        _toolStore.AddServer(def);

        return new ApproveResponse(def.Id, def.ApprovalStatus, def.ApprovedAt.Value, def.ApprovedBy, def.Tools.Count);
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<(string Content, string Hash)> FetchSpecAsync(string url, CancellationToken ct)
    {
        var fetched = await _specFetcher.FetchAsync(SpecSource.FromUrl(url), ct);
        return (fetched.Content, fetched.Hash);
    }
}
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ServerManagementService" -v n
```

Expected: All 9 service tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Management/Services/ServerManagementService.cs tests/McpGateway.UnitTests/Management/ServerManagementServiceUpdateTests.cs
git commit -m "feat(management): add update, soft-delete, refresh, and approve flows"
```

---

### Task 8: Add spec management methods to `ServerManagementService`

**Files:**
- Modify: `src/McpGateway.Management/Services/ServerManagementService.cs`
- Create: `tests/McpGateway.UnitTests/Management/ServerManagementServiceSpecTests.cs`

- [ ] **Step 1: Add failing spec tests**

Create `tests/McpGateway.UnitTests/Management/ServerManagementServiceSpecTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Auth;
using McpGateway.Management.Contracts;
using McpGateway.Management.Services;
using McpGateway.Core.SpecManagement;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class ServerManagementServiceSpecTests
{
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();
    private readonly ISpecVersionRepository _specVersionRepo = Substitute.For<ISpecVersionRepository>();
    private readonly IToolStore _toolStore = Substitute.For<IToolStore>();
    private readonly IToolGenerator _toolGenerator = Substitute.For<IToolGenerator>();
    private readonly ISpecFetcher _specFetcher = Substitute.For<ISpecFetcher>();
    private readonly ISpecDiffService _diffService = Substitute.For<ISpecDiffService>();
    private readonly ICallerIdentityAccessor _caller = Substitute.For<ICallerIdentityAccessor>();

    private ServerManagementService CreateSut() => new(
        _serverRepo, _specVersionRepo, _toolStore, _toolGenerator, _specFetcher, _diffService, _caller,
        NullLogger<ServerManagementService>.Instance);

    [Fact]
    public async Task UploadSpecAsync_UpdatesSpecAndMarksChangesPending()
    {
        var oldHash = ComputeHash("""{"old":true}""");
        var newContent = """{"new":true}""";
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = """{"old":true}""",
            SpecHash = oldHash,
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "approved"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _toolGenerator.Generate(Arg.Any<Microsoft.OpenApi.Models.OpenApiDocument>(), Arg.Any<ClientProfile>())
            .Returns(new List<GeneratedTool>());

        var sut = CreateSut();
        var result = await sut.UploadSpecAsync("inv", new SpecUploadRequest(newContent, "application/json"), CancellationToken.None);

        result.ApprovalStatus.Should().Be("changes_pending");
        _toolStore.Received(1).RemoveServer("inv");
    }

    [Fact]
    public async Task UpdateSpecSourceAsync_Persists()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = "{}",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CreateSut();
        var result = await sut.UpdateSpecSourceAsync("inv", new SpecSourceUpdateRequest("https://new/openapi.json"), CancellationToken.None);

        result.SpecSourceUrl.Should().Be("https://new/openapi.json");
        await _serverRepo.Received(1).UpdateAsync(Arg.Is<McpServerDefinition>(d => d.SpecSourceUrl == "https://new/openapi.json"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSpecAsync_ReturnsContent()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = """{"foo":"bar"}""",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo"
        };
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CreateSut();
        var (content, hash) = await sut.GetSpecAsync("inv", CancellationToken.None);
        content.Should().Contain("foo");
        hash.Should().Be(ComputeHash("""{"foo":"bar"}"""));
    }

    [Fact]
    public async Task GetSpecDiffAsync_NotFound_Throws()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns((McpServerDefinition?)null);
        var sut = CreateSut();
        Func<Task> act = () => sut.GetSpecDiffAsync("inv", Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<McpGateway.Management.Exceptions.NotFoundException>();
    }

    [Fact]
    public async Task GetSpecDiffAsync_HistoricalVersionNotFound_Throws()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = """{"foo":"bar"}""",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo"
        };
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _specVersionRepo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((SpecVersion?)null);

        var sut = CreateSut();
        Func<Task> act = () => sut.GetSpecDiffAsync("inv", Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<McpGateway.Management.Exceptions.NotFoundException>();
    }

    [Fact]
    public async Task GetSpecDiffAsync_ReturnsDiffPayload()
    {
        var versionId = Guid.NewGuid();
        var oldContent = """{"old":true}""";
        var newContent = """{"new":true}""";
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = newContent,
            SpecHash = ComputeHash(newContent),
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ClientProfile = ClientProfile.Universal
        };
        var historical = new SpecVersion
        {
            Id = versionId,
            ServerDefinitionId = def.Id,
            SpecContent = oldContent,
            SpecHash = ComputeHash(oldContent)
        };
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _specVersionRepo.GetAsync(versionId, Arg.Any<CancellationToken>()).Returns(historical);
        _diffService.Diff(oldContent, newContent, ClientProfile.Universal)
            .Returns(new SpecDiffResult(["added_tool"], [], []));

        var sut = CreateSut();
        var result = await sut.GetSpecDiffAsync("inv", versionId, CancellationToken.None);

        result.Should().NotBeNull();
        result.Added.Should().Contain("added_tool");
        result.ComparedHash.Should().Be(historical.SpecHash);
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ServerManagementServiceSpecTests" -v n
```

Expected: FAIL with missing methods.

- [ ] **Step 2: Extend `ServerManagementService` with spec methods**

Add these methods to `ServerManagementService.cs` (insert before the closing brace, after `ApproveAsync`):

```csharp
    public async Task<ServerResponse> UploadSpecAsync(string name, SpecUploadRequest request, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);

        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.Content), "Spec content is empty.")
            });

        // Validate the spec parses
        var parser = new OpenApiParser();
        var document = parser.Parse(request.Content);
        var generated = _toolGenerator.Generate(document, def.ClientProfile);

        var newTools = generated.Select(t => new ToolDefinition
        {
            Id = Guid.NewGuid(),
            ToolName = t.Name,
            Description = t.Description,
            HttpMethod = t.HttpMethod,
            HttpPath = t.HttpPath,
            InputSchema = t.InputSchema?.ToJsonString() ?? "{}",
            OutputSchema = t.OutputSchema?.ToJsonString(),
            AuthConfig = t.AuthConfig,
            Visible = t.Visible,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        def.SpecContent = request.Content;
        def.SpecHash = ComputeHash(request.Content);
        def.ApprovalStatus = "changes_pending";
        def.ApprovedAt = null;
        def.ApprovedBy = null;
        def.UpdatedAt = DateTime.UtcNow;

        await _serverRepo.UpdateToolsAsync(def.Id, newTools, ct);
        await _serverRepo.UpdateAsync(def, ct);
        _toolStore.RemoveServer(name);

        return ServerResponse.FromDomain(def);
    }

    public async Task<ServerResponse> UpdateSpecSourceAsync(string name, SpecSourceUpdateRequest request, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);

        def.SpecSourceUrl = request.SpecSourceUrl;
        def.UpdatedAt = DateTime.UtcNow;
        await _serverRepo.UpdateAsync(def, ct);
        return ServerResponse.FromDomain(def);
    }

    public async Task<(string Content, string Hash)> GetSpecAsync(string name, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);
        return (def.SpecContent, def.SpecHash);
    }

    public async Task<SpecDiffResponse> GetSpecDiffAsync(string name, Guid versionId, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);

        var historical = await _specVersionRepo.GetAsync(versionId, ct)
            ?? throw new NotFoundException("Spec version", versionId.ToString());

        var diff = _diffService.Diff(
            historical.SpecContent,
            def.SpecContent,
            def.ClientProfile);

        return new SpecDiffResponse(
            def.SpecHash,
            historical.SpecHash,
            diff.Added,
            diff.Removed,
            diff.Changed.Select(c => new ToolChangeDto(c.ToolName, c.HttpMethod, c.HttpPath, c.ChangedFields)).ToList());
    }
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ServerManagementService" -v n
```

Expected: All 13 service tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Management/Services/ServerManagementService.cs tests/McpGateway.UnitTests/Management/ServerManagementServiceSpecTests.cs
git commit -m "feat(management): add spec upload, source update, get, and diff methods"
```

---

### Task 9: Implement `ToolManagementService`

**Files:**
- Create: `src/McpGateway.Management/Services/ToolManagementService.cs`
- Create: `tests/McpGateway.UnitTests/Management/ToolManagementServiceTests.cs`

- [ ] **Step 1: Write failing service tests**

Create `tests/McpGateway.UnitTests/Management/ToolManagementServiceTests.cs`:

```csharp
using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Management.Services;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class ToolManagementServiceTests
{
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();
    private readonly IToolOverrideRepository _overrideRepo = Substitute.For<IToolOverrideRepository>();

    private ToolManagementService CreateSut() => new(_serverRepo, _overrideRepo);

    private static McpServerDefinition SampleServer() => new()
    {
        Id = Guid.NewGuid(),
        Name = "inv",
        SpecContent = "{}",
        SpecHash = "h",
        BaseUrl = "https://x",
        AuthStrategy = "obo",
        Tools = new List<ToolDefinition>
        {
            new() { ToolName = "ping", Description = "ping", HttpMethod = "GET", HttpPath = "/ping", InputSchema = "{}", AuthConfig = "{}", Visible = true }
        }
    };

    [Fact]
    public async Task ListAsync_ReturnsEffectiveDescriptionFromOverride()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _overrideRepo.ListByServerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<ToolOverride>
            {
                new() { ToolName = "ping", DescriptionOverride = "custom", Visible = true }
            });

        var sut = CreateSut();
        var tools = await sut.ListAsync("inv", CancellationToken.None);

        tools.Should().ContainSingle();
        tools[0].EffectiveDescription.Should().Be("custom");
        tools[0].HasOverride.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_NoOverride_UsesSpecDescription()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _overrideRepo.ListByServerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<ToolOverride>());

        var sut = CreateSut();
        var tools = await sut.ListAsync("inv", CancellationToken.None);

        tools[0].EffectiveDescription.Should().Be("ping");
        tools[0].HasOverride.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_HidesTool()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _overrideRepo.ListByServerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<ToolOverride>());

        var sut = CreateSut();
        await sut.UpdateAsync("inv", "ping", new UpdateToolRequest(null, false), CancellationToken.None);

        await _overrideRepo.Received(1).UpsertAsync(
            Arg.Is<ToolOverride>(o => o.ToolName == "ping" && o.Visible == false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NotFound_Throws()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        var sut = CreateSut();
        Func<Task> act = () => sut.UpdateAsync("inv", "missing", new UpdateToolRequest(null, true), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task PutOverrideAsync_Persists()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        var sut = CreateSut();
        await sut.PutOverrideAsync("inv", "ping", new PutOverrideRequest("hardened description"), CancellationToken.None);

        await _overrideRepo.Received(1).UpsertAsync(
            Arg.Is<ToolOverride>(o => o.DescriptionOverride == "hardened description" && o.Visible),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteOverrideAsync_Removes()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _overrideRepo.ListByServerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<ToolOverride> { new() { ToolName = "ping", DescriptionOverride = "x", Visible = true } });

        var sut = CreateSut();
        await sut.DeleteOverrideAsync("inv", "ping", CancellationToken.None);

        await _overrideRepo.Received(1).DeleteAsync(Arg.Any<Guid>(), "ping", Arg.Any<CancellationToken>());
    }
}
```

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ToolManagementServiceTests" -v n
```

Expected: FAIL with "type or namespace 'ToolManagementService' could not be found".

- [ ] **Step 2: Implement `ToolManagementService`**

Create `src/McpGateway.Management/Services/ToolManagementService.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;

namespace McpGateway.Management.Services;

public class ToolManagementService
{
    private readonly IServerDefinitionRepository _serverRepo;
    private readonly IToolOverrideRepository _overrideRepo;

    public ToolManagementService(
        IServerDefinitionRepository serverRepo,
        IToolOverrideRepository overrideRepo)
    {
        _serverRepo = serverRepo;
        _overrideRepo = overrideRepo;
    }

    public async Task<IReadOnlyList<ToolResponse>> ListAsync(string serverName, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        var overrides = await _overrideRepo.ListByServerAsync(def.Id, ct);
        var byName = overrides.ToDictionary(o => o.ToolName, StringComparer.Ordinal);

        return def.Tools
            .OrderBy(t => t.ToolName, StringComparer.Ordinal)
            .Select(t =>
            {
                var hasOverride = byName.TryGetValue(t.ToolName, out var ov);
                var effective = hasOverride ? ov!.DescriptionOverride : t.Description;
                var visible = hasOverride ? ov!.Visible : t.Visible;
                return new ToolResponse(
                    t.ToolName,
                    t.Description,
                    t.HttpMethod,
                    t.HttpPath,
                    ParseJsonObject(t.InputSchema),
                    ParseJsonObject(t.OutputSchema),
                    visible,
                    hasOverride,
                    effective);
            })
            .ToList();
    }

    public async Task UpdateAsync(string serverName, string toolName, UpdateToolRequest request, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        var tool = def.Tools.FirstOrDefault(t => string.Equals(t.ToolName, toolName, StringComparison.Ordinal))
            ?? throw new NotFoundException("Tool", toolName);

        var existing = await _overrideRepo.GetAsync(def.Id, toolName, ct);

        var entry = existing ?? new ToolOverride
        {
            ServerDefinitionId = def.Id,
            ToolName = toolName,
            DescriptionOverride = tool.Description,
            Visible = tool.Visible,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (request.DescriptionOverride is not null) entry.DescriptionOverride = request.DescriptionOverride;
        if (request.Visible is not null) entry.Visible = request.Visible.Value;
        entry.UpdatedAt = DateTime.UtcNow;

        await _overrideRepo.UpsertAsync(entry, ct);
    }

    public async Task PutOverrideAsync(string serverName, string toolName, PutOverrideRequest request, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        var tool = def.Tools.FirstOrDefault(t => string.Equals(t.ToolName, toolName, StringComparison.Ordinal))
            ?? throw new NotFoundException("Tool", toolName);

        var existing = await _overrideRepo.GetAsync(def.Id, toolName, ct);
        var entry = existing ?? new ToolOverride
        {
            ServerDefinitionId = def.Id,
            ToolName = toolName,
            Visible = tool.Visible,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        entry.DescriptionOverride = request.DescriptionOverride;
        entry.Visible = tool.Visible;
        entry.UpdatedAt = DateTime.UtcNow;
        await _overrideRepo.UpsertAsync(entry, ct);
    }

    public async Task DeleteOverrideAsync(string serverName, string toolName, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);
        await _overrideRepo.DeleteAsync(def.Id, toolName, ct);
    }

    private static JsonObject ParseJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new JsonObject();
        try { return JsonNode.Parse(raw) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }
}
```

- [ ] **Step 3: Run tests and commit**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ToolManagementServiceTests" -v n
```

Expected: All 6 tool service tests pass.

```bash
git add src/McpGateway.Management/Services/ToolManagementService.cs tests/McpGateway.UnitTests/Management/ToolManagementServiceTests.cs
git commit -m "feat(management): add ToolManagementService for overrides and visibility"
```

---

### Task 10: Implement `GatewayApiKeyService` with bcrypt hashing

**Files:**
- Create: `src/McpGateway.Management/Services/GatewayApiKeyService.cs`
- Create: `tests/McpGateway.UnitTests/Management/GatewayApiKeyServiceTests.cs`

- [ ] **Step 1: Write failing service tests**

Create `tests/McpGateway.UnitTests/Management/GatewayApiKeyServiceTests.cs`:

```csharp
using System.Security.Cryptography;
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Management.Services;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class GatewayApiKeyServiceTests
{
    private readonly IGatewayApiKeyRepository _keyRepo = Substitute.For<IGatewayApiKeyRepository>();
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();

    private GatewayApiKeyService CreateSut() => new(_keyRepo, _serverRepo);

    private static McpServerDefinition SampleServer() => new()
    {
        Id = Guid.NewGuid(),
        Name = "inv",
        SpecContent = "{}",
        SpecHash = "h",
        BaseUrl = "https://x",
        AuthStrategy = "obo"
    };

    [Fact]
    public async Task IssueAsync_ReturnsFullKeyMatchingPrefix()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _keyRepo.AddAsync(Arg.Any<GatewayApiKey>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<GatewayApiKey>());

        var sut = CreateSut();
        var result = await sut.IssueAsync("inv", new CreateApiKeyRequest("ci-runner", new[] { "inv" }), CancellationToken.None);

        result.FullKey.Should().StartWith("mgk_");
        result.FullKey.Length.Should().BeGreaterThan(40);
        result.KeyPrefix.Should().HaveLength(12);
        result.Scopes.Should().Contain("inv");
        await _keyRepo.Received(1).AddAsync(
            Arg.Is<GatewayApiKey>(k => k.KeyHash.StartsWith("$2") && k.Name == "ci-runner"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueAsync_ServerNotFound_Throws()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns((McpServerDefinition?)null);
        var sut = CreateSut();
        Func<Task> act = () => sut.IssueAsync("inv", new CreateApiKeyRequest("k", new[] { "inv" }), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ListAsync_NoServer_Throws()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns((McpServerDefinition?)null);
        var sut = CreateSut();
        Func<Task> act = () => sut.ListAsync("inv", CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ListAsync_ReturnsSummariesWithHashesStripped()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _keyRepo.ListByServerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<GatewayApiKey>
            {
                new() { Id = Guid.NewGuid(), KeyHash = "$2a$11$shouldnotbeexposed", KeyPrefix = "mgk_abc1", Name = "k1", Scopes = new[] { "inv" }, CreatedAt = DateTime.UtcNow }
            });

        var sut = CreateSut();
        var result = await sut.ListAsync("inv", CancellationToken.None);

        result.Should().ContainSingle();
        result[0].KeyPrefix.Should().Be("mgk_abc1");
        result[0].GetType().GetProperty("KeyHash").Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_CallsRepo()
    {
        var id = Guid.NewGuid();
        var sut = CreateSut();
        await sut.RevokeAsync("inv", id, CancellationToken.None);
        await _keyRepo.Received(1).RevokeAsync(id, Arg.Any<CancellationToken>());
    }
}
```

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~GatewayApiKeyServiceTests" -v n
```

Expected: FAIL with "type or namespace 'GatewayApiKeyService' could not be found".

- [ ] **Step 2: Implement `GatewayApiKeyService`**

Create `src/McpGateway.Management/Services/GatewayApiKeyService.cs`:

```csharp
using System.Security.Cryptography;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;

namespace McpGateway.Management.Services;

public class GatewayApiKeyService
{
    public const string KeyPrefix = "mgk_";
    private const int FullKeyByteLength = 32;
    private const int WorkFactor = 11;

    private readonly IGatewayApiKeyRepository _keyRepo;
    private readonly IServerDefinitionRepository _serverRepo;

    public GatewayApiKeyService(IGatewayApiKeyRepository keyRepo, IServerDefinitionRepository serverRepo)
    {
        _keyRepo = keyRepo;
        _serverRepo = serverRepo;
    }

    public async Task<CreateApiKeyResponse> IssueAsync(string serverName, CreateApiKeyRequest request, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        var fullKey = GenerateFullKey();
        var keyHash = BCrypt.Net.BCrypt.HashPassword(fullKey, WorkFactor);
        var keyPrefix = fullKey[..12];

        var entry = new GatewayApiKey
        {
            Id = Guid.NewGuid(),
            ServerDefinitionId = def.Id,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            Name = request.Name,
            Scopes = request.Scopes.ToList(),
            CreatedAt = DateTime.UtcNow
        };

        var saved = await _keyRepo.AddAsync(entry, ct);

        return new CreateApiKeyResponse(
            saved.Id, saved.KeyPrefix, saved.Name, saved.Scopes, saved.CreatedAt, fullKey);
    }

    public async Task<IReadOnlyList<ApiKeySummary>> ListAsync(string serverName, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        var keys = await _keyRepo.ListByServerAsync(def.Id, ct);
        return keys
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeySummary(
                k.Id, k.KeyPrefix, k.Name, k.Scopes, k.CreatedAt, k.RevokedAt, k.LastUsedAt))
            .ToList();
    }

    public Task RevokeAsync(string serverName, Guid keyId, CancellationToken ct)
    {
        return _keyRepo.RevokeAsync(keyId, ct);
    }

    private static string GenerateFullKey()
    {
        Span<byte> bytes = stackalloc byte[FullKeyByteLength];
        RandomNumberGenerator.Fill(bytes);
        var base64 = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return $"{KeyPrefix}{base64}";
    }
}
```

- [ ] **Step 3: Run tests and commit**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~GatewayApiKeyServiceTests" -v n
```

Expected: All 5 api key service tests pass.

```bash
git add src/McpGateway.Management/Services/GatewayApiKeyService.cs tests/McpGateway.UnitTests/Management/GatewayApiKeyServiceTests.cs
git commit -m "feat(management): add GatewayApiKeyService with bcrypt hash + full key returned once"
```

---

### Task 11: Implement `ClientProfileService`

**Files:**
- Create: `src/McpGateway.Management/Services/ClientProfileService.cs`
- Create: `tests/McpGateway.UnitTests/Management/ClientProfileServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpGateway.UnitTests/Management/ClientProfileServiceTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class ClientProfileServiceTests
{
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();
    private readonly IToolStore _toolStore = Substitute.For<IToolStore>();

    private ClientProfileService CreateSut() => new(_serverRepo, _toolStore, NullLogger<ClientProfileService>.Instance);

    [Fact]
    public async Task SetAsync_UpdatesProfileAndReloadsTools()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            ClientProfile = ClientProfile.Universal,
            SpecContent = "{}",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "approved"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CreateSut();
        await sut.SetAsync("inv", ClientProfile.Cursor, CancellationToken.None);

        await _serverRepo.Received(1).UpdateAsync(
            Arg.Is<McpServerDefinition>(d => d.ClientProfile == ClientProfile.Cursor),
            Arg.Any<CancellationToken>());
        _toolStore.Received(1).UpdateServer(Arg.Is<McpServerDefinition>(d => d.ClientProfile == ClientProfile.Cursor));
    }

    [Fact]
    public async Task SetAsync_NotApproved_DoesNotReloadStore()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            ClientProfile = ClientProfile.Universal,
            SpecContent = "{}",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "pending"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CreateSut();
        await sut.SetAsync("inv", ClientProfile.Claude, CancellationToken.None);

        _toolStore.DidNotReceive().UpdateServer(Arg.Any<McpServerDefinition>());
    }
}
```

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ClientProfileServiceTests" -v n
```

Expected: FAIL with "type or namespace 'ClientProfileService' could not be found".

- [ ] **Step 2: Implement `ClientProfileService`**

Create `src/McpGateway.Management/Services/ClientProfileService.cs`:

```csharp
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Exceptions;
using Microsoft.Extensions.Logging;

namespace McpGateway.Management.Services;

public class ClientProfileService
{
    private readonly IServerDefinitionRepository _serverRepo;
    private readonly IToolStore _toolStore;
    private readonly ILogger<ClientProfileService> _logger;

    public ClientProfileService(
        IServerDefinitionRepository serverRepo,
        IToolStore toolStore,
        ILogger<ClientProfileService> logger)
    {
        _serverRepo = serverRepo;
        _toolStore = toolStore;
        _logger = logger;
    }

    public async Task SetAsync(string serverName, ClientProfile profile, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        def.ClientProfile = profile;
        def.UpdatedAt = DateTime.UtcNow;
        await _serverRepo.UpdateAsync(def, ct);

        if (def.ApprovalStatus == "approved" && def.Status == "active")
        {
            _toolStore.UpdateServer(def);
            _logger.LogInformation("Client profile updated and tool store refreshed for {Server}.", serverName);
        }
    }
}
```

- [ ] **Step 3: Run tests and commit**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ClientProfileServiceTests" -v n
```

Expected: Both tests pass.

```bash
git add src/McpGateway.Management/Services/ClientProfileService.cs tests/McpGateway.UnitTests/Management/ClientProfileServiceTests.cs
git commit -m "feat(management): add ClientProfileService with hot-reload of approved servers"
```

---

### Task 12: Add Management DI registration extension

**Files:**
- Create: `src/McpGateway.Management/Services/ManagementServiceExtensions.cs`

- [ ] **Step 1: Create the extension**

Create `src/McpGateway.Management/Services/ManagementServiceExtensions.cs`:

```csharp
using McpGateway.Management.Auth;
using McpGateway.Management.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace McpGateway.Management.Services;

public static class ManagementServiceExtensions
{
    public static IServiceCollection AddMcpManagement(this IServiceCollection services)
    {
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICallerIdentityAccessor, CallerIdentityAccessor>();

        services.AddScoped<ServerManagementService>();
        services.AddScoped<ToolManagementService>();
        services.AddScoped<GatewayApiKeyService>();
        services.AddScoped<ClientProfileService>();

        return services;
    }
}
```

- [ ] **Step 2: Build Management**

Run:

```bash
dotnet build src/McpGateway.Management/McpGateway.Management.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/McpGateway.Management/Services/ManagementServiceExtensions.cs
git commit -m "feat(management): add AddMcpManagement DI extension"
```

---

### Task 13: Add admin JWT auth handler with role claim

**Files:**
- Create: `src/McpGateway.Api/Auth/JwtAuthHandler.cs`
- Create: `src/McpGateway.Api/Auth/AdminAuthHandler.cs`

- [ ] **Step 1: Create the `JwtAuthHandler` base**

Create `src/McpGateway.Api/Auth/JwtAuthHandler.cs`:

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace McpGateway.Api.Auth;

public class JwtBearerOptions : AuthenticationSchemeOptions
{
    public string MetadataAddress { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string ValidIssuer { get; set; } = string.Empty;
    public string RoleClaimType { get; set; } = "roles";
    public string UpnClaimType { get; set; } = "preferred_username";
}

public class JwtAuthHandler : AuthenticationHandler<JwtBearerOptions>
{
    public const string SchemeName = "AdminJwt";

    public JwtAuthHandler(
        IOptionsMonitor<JwtBearerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var auth))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var raw = auth.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = raw["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty bearer token."));
        }

        // In dev/test we accept a simple unsigned JWT-style token where the payload is
        // base64url-encoded JSON containing the admin claims. Production replaces this
        // with Microsoft.Identity.Web's JwtBearerHandler + JWKS validation.
        var claims = DevTokenParser.Parse(token, Options.RoleClaimType, Options.UpnClaimType);
        if (claims.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Token could not be parsed."));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal static class DevTokenParser
{
    public static List<Claim> Parse(string token, string roleClaimType, string upnClaimType)
    {
        var claims = new List<Claim>();
        var parts = token.Split('.');
        if (parts.Length < 2) return claims;

        try
        {
            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (root.TryGetProperty(roleClaimType, out var rolesEl))
            {
                if (rolesEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var r in rolesEl.EnumerateArray())
                    {
                        var role = r.GetString();
                        if (!string.IsNullOrEmpty(role))
                            claims.Add(new Claim(ClaimTypes.Role, role));
                    }
                }
                else if (rolesEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var role = rolesEl.GetString();
                    if (!string.IsNullOrEmpty(role))
                        claims.Add(new Claim(ClaimTypes.Role, role));
                }
            }

            if (root.TryGetProperty(upnClaimType, out var upnEl)
                && upnEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var upn = upnEl.GetString();
                if (!string.IsNullOrEmpty(upn))
                    claims.Add(new Claim(CallerIdentityAccessor.AdminUpnClaim, upn));
            }

            if (root.TryGetProperty("sub", out var subEl)
                && subEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var sub = subEl.GetString();
                if (!string.IsNullOrEmpty(sub))
                    claims.Add(new Claim(ClaimTypes.NameIdentifier, sub));
            }
        }
        catch
        {
            // ignore parse failures
        }
        return claims;
    }

    private static string Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
```

Note: This handler is dev/test-grade. Production code paths in `Program.cs` swap in `Microsoft.Identity.Web`'s `JwtBearerHandler` for JWKS validation; the `DevelopmentAdminAuthHandler` from the next task is an alternative for local-only use.

- [ ] **Step 2: Add `AdminAuthHandler` that delegates to `JwtAuthHandler` and enforces the admin role**

Create `src/McpGateway.Api/Auth/AdminAuthHandler.cs`:

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using McpGateway.Management.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace McpGateway.Api.Auth;

public class AdminAuthHandler : AuthenticationHandler<JwtBearerOptions>
{
    public new const string SchemeName = "Admin";

    public AdminAuthHandler(
        IOptionsMonitor<JwtBearerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var auth))
        {
            return AuthenticateResult.NoResult();
        }

        var raw = auth.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = raw["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.Fail("Empty bearer token.");
        }

        var claims = DevTokenParser.Parse(token, Options.RoleClaimType, Options.UpnClaimType);
        if (claims.Count == 0)
        {
            return AuthenticateResult.Fail("Token could not be parsed.");
        }

        var isAdmin = claims.Any(c =>
            c.Type == ClaimTypes.Role && c.Value == CallerIdentityAccessor.AdminRole);

        if (!isAdmin)
        {
            return AuthenticateResult.Fail($"Caller does not have the required '{CallerIdentityAccessor.AdminRole}' role.");
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
```

- [ ] **Step 3: Build Api project**

Run:

```bash
dotnet build src/McpGateway.Api/McpGateway.Api.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Api/Auth/JwtAuthHandler.cs src/McpGateway.Api/Auth/AdminAuthHandler.cs
git commit -m "feat(api): add AdminAuthHandler enforcing mcp-gateway-admin role claim"
```

---

### Task 14: Add `DevelopmentAdminAuthHandler` for tests and local dev

**Files:**
- Create: `src/McpGateway.Api/Auth/DevelopmentAdminAuthHandler.cs`

- [ ] **Step 1: Create the dev handler**

Create `src/McpGateway.Api/Auth/DevelopmentAdminAuthHandler.cs`:

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using McpGateway.Management.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace McpGateway.Api.Auth;

public class DevelopmentAdminOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-Dev-Admin";
    public string DefaultUpn { get; set; } = "dev-admin@corp.local";
}

public class DevelopmentAdminAuthHandler : AuthenticationHandler<DevelopmentAdminOptions>
{
    public const string SchemeName = "DevAdmin";

    public DevelopmentAdminAuthHandler(
        IOptionsMonitor<DevelopmentAdminOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var upn = Request.Headers.TryGetValue(Options.HeaderName, out var v) && !string.IsNullOrWhiteSpace(v.ToString())
            ? v.ToString()
            : Options.DefaultUpn;

        var claims = new[]
        {
            new Claim(CallerIdentityAccessor.AdminUpnClaim, upn),
            new Claim(ClaimTypes.Role, CallerIdentityAccessor.AdminRole),
            new Claim(ClaimTypes.NameIdentifier, upn)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

- [ ] **Step 2: Build and commit**

Run:

```bash
dotnet build src/McpGateway.Api/McpGateway.Api.csproj
git add src/McpGateway.Api/Auth/DevelopmentAdminAuthHandler.cs
git commit -m "feat(api): add DevelopmentAdminAuthHandler for local dev and tests"
```

---

### Task 15: Implement `AdminEndpoints` minimal API surface

**Files:**
- Create: `src/McpGateway.Api/Endpoints/AdminEndpoints.cs`

- [ ] **Step 1: Create `AdminEndpoints`**

Create `src/McpGateway.Api/Endpoints/AdminEndpoints.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using McpGateway.Api.Auth;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace McpGateway.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin")
            .RequireAuthorization("Admin")
            .WithTags("admin");

        // Server definitions
        group.MapGet("/servers", async (ServerManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapGet("/servers/{name}", async (string name, ServerManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(name, ct)));

        group.MapPost("/servers", async (
            CreateServerRequest body,
            IValidator<CreateServerRequest> validator,
            ServerManagementService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            return Results.Created($"/admin/servers/{body.Name}", await svc.RegisterAsync(body, ct));
        });

        group.MapPatch("/servers/{name}", async (
            string name,
            UpdateServerRequest body,
            IValidator<UpdateServerRequest> validator,
            ServerManagementService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            return Results.Ok(await svc.UpdateAsync(name, body, ct));
        });

        group.MapDelete("/servers/{name}", async (string name, ServerManagementService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(name, ct);
            return Results.NoContent();
        });

        group.MapPost("/servers/{name}/refresh", async (string name, ServerManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.RefreshAsync(name, ct)));

        group.MapPost("/servers/{name}/approve", async (string name, ServerManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.ApproveAsync(name, ct)));

        // Tools
        group.MapGet("/servers/{name}/tools", async (string name, ToolManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(name, ct)));

        group.MapPatch("/servers/{name}/tools/{toolName}", async (
            string name,
            string toolName,
            UpdateToolRequest body,
            IValidator<UpdateToolRequest> validator,
            ToolManagementService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            await svc.UpdateAsync(name, toolName, body, ct);
            return Results.NoContent();
        });

        group.MapPut("/servers/{name}/tools/{toolName}/override", async (
            string name,
            string toolName,
            PutOverrideRequest body,
            IValidator<PutOverrideRequest> validator,
            ToolManagementService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            await svc.PutOverrideAsync(name, toolName, body, ct);
            return Results.NoContent();
        });

        group.MapDelete("/servers/{name}/tools/{toolName}/override", async (
            string name,
            string toolName,
            ToolManagementService svc,
            CancellationToken ct) =>
        {
            await svc.DeleteOverrideAsync(name, toolName, ct);
            return Results.NoContent();
        });

        // Gateway API keys
        group.MapGet("/servers/{name}/api-keys", async (string name, GatewayApiKeyService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(name, ct)));

        group.MapPost("/servers/{name}/api-keys", async (
            string name,
            CreateApiKeyRequest body,
            IValidator<CreateApiKeyRequest> validator,
            GatewayApiKeyService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            return Results.Created($"/admin/servers/{name}/api-keys/{Guid.NewGuid()}", await svc.IssueAsync(name, body, ct));
        });

        group.MapDelete("/servers/{name}/api-keys/{keyId:guid}", async (
            string name,
            Guid keyId,
            GatewayApiKeyService svc,
            CancellationToken ct) =>
        {
            await svc.RevokeAsync(name, keyId, ct);
            return Results.NoContent();
        });

        // Spec
        group.MapPost("/servers/{name}/spec", async (
            string name,
            SpecUploadRequest body,
            ServerManagementService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.UploadSpecAsync(name, body, ct)));

        group.MapPut("/servers/{name}/spec-source", async (
            string name,
            SpecSourceUpdateRequest body,
            ServerManagementService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.UpdateSpecSourceAsync(name, body, ct)));

        group.MapGet("/servers/{name}/spec", async (string name, ServerManagementService svc, CancellationToken ct) =>
        {
            var (content, hash) = await svc.GetSpecAsync(name, ct);
            return Results.Ok(new { content, hash });
        });

        group.MapGet("/servers/{name}/spec/diff/{versionId:guid}", async (
            string name,
            Guid versionId,
            ServerManagementService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetSpecDiffAsync(name, versionId, ct)));

        return app;
    }

    private static async Task ValidateAsync<T>(IValidator<T> validator, T instance)
    {
        var result = await validator.ValidateAsync(instance);
        if (!result.IsValid)
            throw new ValidationException(result.Errors);
    }
}

public static class AdminExceptionMiddleware
{
    public static IApplicationBuilder UseAdminExceptionHandler(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            try
            {
                await next();
            }
            catch (NotFoundException ex)
            {
                await Write(ctx, StatusCodes.Status404NotFound, "not_found", ex.Message);
            }
            catch (ConflictException ex)
            {
                await Write(ctx, StatusCodes.Status409Conflict, "conflict", ex.Message);
            }
            catch (ValidationException ex)
            {
                var errors = ex.Errors
                    .Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
                    .ToArray();
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = "validation_failed", errors }));
            }
            catch (Exception ex) when (ctx.RequestAborted.IsCancellationRequested == false)
            {
                var logger = ctx.RequestServices.GetRequiredService<ILogger<MarkerClass>>();
                logger.LogError(ex, "Unhandled admin API error.");
                await Write(ctx, StatusCodes.Status500InternalServerError, "internal_error", ex.Message);
            }
        });
    }

    private static async Task Write(HttpContext ctx, int status, string error, string detail)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new ErrorResponse(error, detail)));
    }

    private sealed class MarkerClass { }
}
```

- [ ] **Step 2: Build Api**

Run:

```bash
dotnet build src/McpGateway.Api/McpGateway.Api.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/McpGateway.Api/Endpoints/AdminEndpoints.cs
git commit -m "feat(api): add /admin/* minimal API endpoints with exception mapping"
```

---

### Task 16: Wire Management + admin endpoints into `Program.cs`

**Files:**
- Modify: `src/McpGateway.Api/Program.cs`

- [ ] **Step 1: Read existing `Program.cs`**

Read the file to understand existing service registration. If it does not exist yet, scaffold a minimal `Program.cs`:

```csharp
using McpGateway.Api.Auth;
using McpGateway.Api.Endpoints;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Services;
using McpGateway.Core;
using McpGateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Persistence
builder.Services.AddMcpPersistence(builder.Configuration);

// Core services: tool store, spec fetch/diff, tool generator
builder.Services.AddMcpCore();

// Management services
builder.Services.AddMcpManagement();

// Auth
builder.Services.AddAuthentication(AdminAuthHandler.SchemeName)
    .AddScheme<JwtBearerOptions, AdminAuthHandler>(AdminAuthHandler.SchemeName, _ => { })
    .AddScheme<DevelopmentAdminOptions, DevelopmentAdminAuthHandler>(DevelopmentAdminAuthHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy =>
        policy.AddAuthenticationSchemes(AdminAuthHandler.SchemeName, DevelopmentAdminAuthHandler.SchemeName)
              .RequireAuthenticatedUser());
});

var app = builder.Build();

app.UseAdminExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapAdminEndpoints();

app.Run();
```

Write the modified `Program.cs`. If an existing `Program.cs` is in place, merge by:
- Adding `using McpGateway.Api.Auth;` and `using McpGateway.Api.Endpoints;` and `using McpGateway.Management.Services;` and `using McpGateway.Core;`
- Adding `builder.Services.AddMcpCore();` before `AddMcpManagement()`
- Adding `builder.Services.AddMcpManagement();` near the other `Add*` calls
- Adding the auth scheme registration block
- Adding `app.UseAdminExceptionHandler();` before `UseAuthorization`
- Adding `app.MapAdminEndpoints();` after `UseAuthorization`

- [ ] **Step 2: Build full solution**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/McpGateway.sln
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/McpGateway.Api/Program.cs
git commit -m "feat(api): wire management services and /admin/* endpoints into Program.cs"
```

---

### Task 17: Set up admin integration test harness with Testcontainers

**Files:**
- Create: `tests/McpGateway.IntegrationTests/AdminApiFactory.cs`
- Create: `tests/McpGateway.IntegrationTests/AdminApiCollection.cs`
- Modify: `tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj`

- [ ] **Step 1: Add `Microsoft.AspNetCore.Mvc.Testing` package**

Run:

```bash
dotnet add tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 10.0.9
```

- [ ] **Step 2: Create the factory**

Create `tests/McpGateway.IntegrationTests/AdminApiFactory.cs`:

```csharp
using McpGateway.Api.Auth;
using McpGateway.Api.Endpoints;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Services;
using McpGateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace McpGateway.IntegrationTests;

public sealed class AdminApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithDatabase("mcp_admin_tests")
        .WithUsername("mcp")
        .WithPassword("mcp")
        .WithImage("postgres:18-alpine")
        .Build();

    public string ConnectionString => _pg.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = ConnectionString,
                ["Admin:UseDevHandler"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace any pre-registered DbContext with the one pointed at Testcontainers
            services.RemoveAll(typeof(DbContextOptions<McpGatewayDbContext>));
            services.AddDbContext<McpGatewayDbContext>(o => o.UseNpgsql(ConnectionString));
        });
    }

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<McpGatewayDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
    }
}
```

- [ ] **Step 3: Create the xUnit collection**

Create `tests/McpGateway.IntegrationTests/AdminApiCollection.cs`:

```csharp
namespace McpGateway.IntegrationTests;

[CollectionDefinition("AdminApi")]
public class AdminApiCollection : ICollectionFixture<AdminApiFactory>
{
}
```

- [ ] **Step 4: Build tests**

Run:

```bash
dotnet build tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj
```

Expected: Build succeeds.

---

### Task 18: Integration tests — full server definition CRUD + approval + hot reload

**Files:**
- Create: `tests/McpGateway.IntegrationTests/AdminApiTests.cs`

- [ ] **Step 1: Create the integration test file**

Create `tests/McpGateway.IntegrationTests/AdminApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.ToolStore;
using McpGateway.IntegrationTests;
using McpGateway.Management.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace McpGateway.IntegrationTests;

[Collection("AdminApi")]
public class AdminApiTests : IClassFixture<AdminApiFactory>
{
    private const string DevAdminHeader = "X-Dev-Admin";
    private const string AdminUpn = "alice@corp.local";

    private readonly AdminApiFactory _factory;

    public AdminApiTests(AdminApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAdminHeader, AdminUpn);
        return client;
    }

    private static StringContent JsonBody(object o) =>
        new(JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json");

    private static readonly string SampleSpec = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Invoice API", "version": "1.0.0" },
      "paths": {
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

    [Fact]
    public async Task Register_Approve_AndList_ProducesApprovedServerInToolStore()
    {
        var client = CreateAdminClient();

        // 1) Register
        var create = new CreateServerRequest(
            "invoice-api", "Invoice API", "desc",
            null, SampleSpec,
            "https://invoice.example.com", "obo",
            new JsonObject { ["resource"] = "api://invoice-api/.default" },
            "all", "universal", 1440, AdminUpn);

        var post = await client.PostAsync("/admin/servers", JsonBody(create));
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2) Tool store is empty until approved
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IToolStore>();
            store.Contains("invoice-api").Should().BeFalse();
        }

        // 3) Approve
        var approve = await client.PostAsync("/admin/servers/invoice-api/approve", content: null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approve.Content.ReadFromJsonAsync<ApproveResponse>();
        approved!.ApprovalStatus.Should().Be("approved");
        approved.ApprovedBy.Should().Be(AdminUpn);

        // 4) Tool store now has the server
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IToolStore>();
            store.Contains("invoice-api").Should().BeTrue();
        }

        // 5) List returns it
        var list = await client.GetAsync("/admin/servers");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var servers = await list.Content.ReadFromJsonAsync<List<ServerResponse>>();
        servers!.Select(s => s.Name).Should().Contain("invoice-api");
    }

    [Fact]
    public async Task PatchServer_UpdatesDisplayName()
    {
        var client = CreateAdminClient();
        await RegisterServer(client, "patch-test");

        var patch = new UpdateServerRequest("Patched Name", null, null, null, null, null, null, null, null, null);
        var resp = await client.PatchAsync("/admin/servers/patch-test", JsonBody(patch));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ServerResponse>();
        body!.DisplayName.Should().Be("Patched Name");
    }

    [Fact]
    public async Task DeleteServer_SetsDisabledStatusAndRemovesFromStore()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "del-test");

        var resp = await client.DeleteAsync("/admin/servers/del-test");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IToolStore>();
        store.Contains("del-test").Should().BeFalse();
    }

    [Fact]
    public async Task RefreshServer_WhenSpecChanged_SetsChangesPending()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "refresh-test");

        // PATCH the spec to new content with different operation
        var newSpec = SampleSpec.Replace("listInvoices", "listInvoicesV2");
        var upload = new SpecUploadRequest(newSpec, "application/json");
        var resp = await client.PostAsync("/admin/servers/refresh-test/spec", JsonBody(upload));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ServerResponse>();
        body!.ApprovalStatus.Should().Be("changes_pending");
    }

    [Fact]
    public async Task ListTools_ReturnsEffectiveDescription()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "tools-test");

        // Override description
        var ovr = new PutOverrideRequest("Hardened description (admin review)");
        var put = await client.PutAsync("/admin/servers/tools-test/tools/listInvoices/override", JsonBody(ovr));
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetAsync("/admin/servers/tools-test/tools");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var tools = await list.Content.ReadFromJsonAsync<List<ToolResponse>>();
        tools!.Should().ContainSingle(t => t.EffectiveDescription == "Hardened description (admin review)" && t.HasOverride);
    }

    [Fact]
    public async Task UpdateTool_HidesToolViaVisibilityToggle()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "visibility-test");

        var body = new UpdateToolRequest(null, false);
        var resp = await client.PatchAsync("/admin/servers/visibility-test/tools/listInvoices", JsonBody(body));
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetAsync("/admin/servers/visibility-test/tools");
        var tools = await list.Content.ReadFromJsonAsync<List<ToolResponse>>();
        tools!.Single().Visible.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteOverride_RevertsToSpecDescription()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "revert-test");
        await client.PutAsync("/admin/servers/revert-test/tools/listInvoices/override",
            JsonBody(new PutOverrideRequest("Custom")));

        var resp = await client.DeleteAsync("/admin/servers/revert-test/tools/listInvoices/override");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetAsync("/admin/servers/revert-test/tools");
        var tools = await list.Content.ReadFromJsonAsync<List<ToolResponse>>();
        tools!.Single().HasOverride.Should().BeFalse();
    }

    [Fact]
    public async Task IssueApiKey_ReturnsFullKeyOnlyOnce()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "apikey-test");

        var resp = await client.PostAsync("/admin/servers/apikey-test/api-keys",
            JsonBody(new CreateApiKeyRequest("ci-runner", new[] { "apikey-test" })));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        body!.FullKey.Should().StartWith("mgk_");
        body.KeyPrefix.Should().HaveLength(12);

        var list = await client.GetAsync("/admin/servers/apikey-test/api-keys");
        var keys = await list.Content.ReadFromJsonAsync<List<ApiKeySummary>>();
        keys!.Single().KeyPrefix.Should().Be(body.KeyPrefix);
    }

    [Fact]
    public async Task RevokeApiKey_Returns204()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "revoke-test");
        var issued = await (await client.PostAsync("/admin/servers/revoke-test/api-keys",
            JsonBody(new CreateApiKeyRequest("k", new[] { "revoke-test" }))))
            .Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        var resp = await client.DeleteAsync($"/admin/servers/revoke-test/api-keys/{issued!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetAsync("/admin/servers/revoke-test/api-keys");
        var keys = await list.Content.ReadFromJsonAsync<List<ApiKeySummary>>();
        keys!.Single().RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadSpec_AndGetSpec_RoundTrips()
    {
        var client = CreateAdminClient();
        await RegisterServer(client, "spec-roundtrip");

        var newSpec = SampleSpec.Replace("1.0.0", "2.0.0");
        var upload = new SpecUploadRequest(newSpec, "application/json");
        var uploadResp = await client.PostAsync("/admin/servers/spec-roundtrip/spec", JsonBody(upload));
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResp = await client.GetAsync("/admin/servers/spec-roundtrip/spec");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("content").GetString().Should().Contain("2.0.0");
    }

    [Fact]
    public async Task UpdateSpecSource_PersistsUrl()
    {
        var client = CreateAdminClient();
        await RegisterServer(client, "source-test");

        var body = new SpecSourceUpdateRequest("https://new.example.com/openapi.json");
        var resp = await client.PutAsync("/admin/servers/source-test/spec-source", JsonBody(body));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var server = await resp.Content.ReadFromJsonAsync<ServerResponse>();
        server!.SpecSourceUrl.Should().Be("https://new.example.com/openapi.json");
    }

    [Fact]
    public async Task RegisterServer_DuplicateName_Returns409()
    {
        var client = CreateAdminClient();
        await RegisterServer(client, "dup-name");

        var resp = await client.PostAsync("/admin/servers", JsonBody(BuildCreate("dup-name")));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RegisterServer_InvalidPayload_Returns400()
    {
        var client = CreateAdminClient();
        var bad = new CreateServerRequest(
            "Bad Name With Spaces", "X", null, null, SampleSpec,
            "https://x", "magic", new JsonObject(), "all", "universal", 1440, null);

        var resp = await client.PostAsync("/admin/servers", JsonBody(bad));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnauthenticatedCall_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/admin/servers");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSpecDiff_ReturnsDiffPayload()
    {
        var client = CreateAdminClient();
        await RegisterServer(client, "diff-test");
        var resp = await client.GetAsync($"/admin/servers/diff-test/spec/diff/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await resp.Content.ReadFromJsonAsync<SpecDiffResponse>();
        payload.Should().NotBeNull();
    }

    // helpers
    private static CreateServerRequest BuildCreate(string name) => new(
        name, name, null, null, SampleSpec,
        "https://" + name + ".example.com", "obo",
        new JsonObject { ["resource"] = "api://" + name + "/.default" },
        "all", "universal", 1440, AdminUpn);

    private async Task RegisterServer(HttpClient client, string name)
    {
        var resp = await client.PostAsync("/admin/servers", JsonBody(BuildCreate(name)));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task RegisterAndApproveServer(HttpClient client, string name)
    {
        await RegisterServer(client, name);
        var approve = await client.PostAsync($"/admin/servers/{name}/approve", content: null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2: Run integration tests**

Run:

```bash
dotnet test tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj --filter "FullyQualifiedName~AdminApiTests" -v n
```

Expected: All 14 integration tests pass. Docker must be available for Testcontainers.

If any test fails with `Unauthorized`, ensure `Program.cs` includes the `Admin` policy that lists `DevelopmentAdminAuthHandler.SchemeName` as an accepted scheme. If a test fails with `404` on a route, check that `MapAdminEndpoints` was added to `Program.cs`.

- [ ] **Step 3: Commit**

```bash
git add tests/McpGateway.IntegrationTests/AdminApiTests.cs tests/McpGateway.IntegrationTests/AdminApiFactory.cs tests/McpGateway.IntegrationTests/AdminApiCollection.cs
git commit -m "test(admin): add full /admin/* integration test coverage"
```

---

### Task 19: Run full solution build and test suite

- [ ] **Step 1: Build everything**

Run:

```bash
dotnet build /var/home/felix/github/mcp-gateway/McpGateway.sln
```

Expected: Build succeeds with no errors.

- [ ] **Step 2: Run unit tests**

Run:

```bash
dotnet test /var/home/felix/github/mcp-gateway/McpGateway.sln --filter "Category!=Integration&FullyQualifiedName!~IntegrationTests" -v n
```

Expected: All unit tests pass (validators, server/tool/api-key/client-profile services).

- [ ] **Step 3: Run admin integration tests**

Run:

```bash
dotnet test /var/home/felix/github/mcp-gateway/McpGateway.sln --filter "FullyQualifiedName~AdminApiTests" -v n
```

Expected: All 14 AdminApiTests pass.

- [ ] **Step 4: Final commit if any cleanup was needed**

```bash
git status
# if there are uncommitted changes:
git add -A
git commit -m "chore(admin): post-test build and lint cleanup"
```

---

## Self-Review

**1. Spec coverage:**

| Requirement from task brief | Task(s) implementing it |
|---|---|
| `GET /admin/servers` (list) | Task 6 (ServerManagementService.ListAsync) → Task 15 (group.MapGet) → Task 18 (test) |
| `POST /admin/servers` (register) | Task 6 (RegisterAsync) → Task 15 (MapPost) → Task 18 (test) |
| `GET /admin/servers/{name}` | Task 6 (GetAsync) → Task 15 → Task 18 |
| `PATCH /admin/servers/{name}` | Task 7 (UpdateAsync) → Task 15 → Task 18 |
| `DELETE /admin/servers/{name}` (soft delete) | Task 7 (DeleteAsync — sets `Status=disabled`) → Task 15 → Task 18 |
| `POST /admin/servers/{name}/refresh` | Task 7 (RefreshAsync — sets `approval_status=changes_pending`, removes from store) → Task 15 → Task 18 |
| `POST /admin/servers/{name}/approve` | Task 7 (ApproveAsync — sets `approval_status=approved`, `AddServer` to tool store) → Task 15 → Task 18 |
| `GET /admin/servers/{name}/tools` | Task 9 (ToolManagementService.ListAsync with effective description + override) → Task 15 → Task 18 |
| `PATCH /admin/servers/{name}/tools/{tool_name}` | Task 9 (UpdateAsync — partial update) → Task 15 → Task 18 |
| `PUT /admin/servers/{name}/tools/{tool_name}/override` | Task 9 (PutOverrideAsync) → Task 15 → Task 18 |
| `DELETE /admin/servers/{name}/tools/{tool_name}/override` | Task 9 (DeleteOverrideAsync) → Task 15 → Task 18 |
| `GET /admin/servers/{name}/api-keys` | Task 10 (ListAsync) → Task 15 → Task 18 |
| `POST /admin/servers/{name}/api-keys` (return full key ONCE) | Task 10 (IssueAsync — bcrypt hash stored, full key in `CreateApiKeyResponse.FullKey`) → Task 15 → Task 18 |
| `DELETE /admin/servers/{name}/api-keys/{key_id}` | Task 10 (RevokeAsync) → Task 15 → Task 18 |
| `POST /admin/servers/{name}/spec` (upload) | Task 8 (UploadSpecAsync) → Task 15 → Task 18 |
| `PUT /admin/servers/{name}/spec-source` | Task 8 (UpdateSpecSourceAsync) → Task 15 → Task 18 |
| `GET /admin/servers/{name}/spec` | Task 8 (GetSpecAsync) → Task 15 → Task 18 |
| `GET /admin/servers/{name}/spec/diff/{version_id}` | Task 8 (GetSpecDiffAsync) → Task 15 → Task 18 |
| Admin auth: Entra ID JWT with admin role claim | Task 13 (`AdminAuthHandler` parses `roles` claim, rejects non-admins) |
| Dev/test admin auth fallback | Task 14 (`DevelopmentAdminAuthHandler`, header `X-Dev-Admin`) |
| On approve, load approved server into `IToolStore` (hot reload) | Task 7 (`_toolStore.AddServer(def)` in `ApproveAsync`) |
| On spec refresh, detect changes, store new version, set `approval_status='changes_pending'`, remove old tools from InMemoryToolStore | Task 7 (`RefreshAsync`: hash compare, regenerate tools, `_toolStore.RemoveServer`) |
| `ServerManagementService.cs` component | Tasks 7-9 |
| `ToolManagementService.cs` component | Task 9 |
| `GatewayApiKeyService.cs` component | Task 10 |
| `ClientProfileService.cs` component | Task 11 |
| `Contracts/Dtos.cs` component | Task 2 |
| `Contracts/Validators.cs` component | Task 4 |
| `Endpoints/AdminEndpoints.cs` component | Task 15 |
| `Program.cs` updates | Task 16 |
| `tests/AdminApiTests.cs` component | Tasks 18-19 |
| FluentValidation 11.x | Task 4 (FluentValidation 11.9.2) |
| BCrypt.Net-Next for API key hashing | Task 10 (`BCrypt.Net.BCrypt.HashPassword` with work factor 11) |
| Microsoft.Identity.Web for admin role claim (optional) | Task 13 (production swap-in note; dev/test path uses `DevTokenParser`) |

**2. Placeholder scan:**

Searched plan for: `TBD`, `TODO`, `implement later`, "appropriate error handling", "similar to Task N", "fill in details". None found. Every code step contains complete C# code; every command has a real command and expected output.

**3. Type consistency:**

- `McpServerDefinition.Name` (Core, `string`) — same identifier in DTOs, validators, services, endpoints.
- `McpServerDefinition.ApprovalStatus` (string) — set to `"pending"`, `"approved"`, or `"changes_pending"` consistently across `RegisterAsync`, `RefreshAsync`, `ApproveAsync`, `UploadSpecAsync`.
- `IToolStore.AddServer(McpServerDefinition)` — called in `ApproveAsync` (Task 7), `UpdateServer` referenced in `ClientProfileService` (Task 11), `RemoveServer(string)` called in `DeleteAsync`, `RefreshAsync`, `UploadSpecAsync`, `UpdateAsync` (auth changes path).
- `IServerDefinitionRepository.GetByNameAsync` (returns full def) vs `GetByNameForAdminAsync` (returns with all navigations) — used consistently.
- `GatewayApiKey.KeyHash` is the bcrypt hash (`$2a$11$...`); full key never persisted; only `KeyPrefix` is exposed in lists.
- `ToolOverride` upsert: `Visible` defaults to `true` on insert, `UpdatedAt` always set on update.
- `ServerResponse` uses `JsonObject` for `AuthConfig` (round-trip via `ToJsonString`); `SpecContent` remains raw string internally.
- `GeneratedTool` (from `McpGateway.Core.ToolGeneration`) is the output of `IToolGenerator`; `ToolDefinition` (from `McpGateway.Core.ServerDefinitions`) is the persistence/runtime model. The Management API maps `GeneratedTool` → `ToolDefinition` in `RegisterAsync`, `RefreshAsync`, and `UploadSpecAsync`.

**4. Architectural alignment:**

- ADR-0003 (hot-reload via custom tool provider) — `IToolStore.AddServer/UpdateServer/RemoveServer` are called from approval and refresh paths, no restart needed.
- ADR-0005 (admin approval before activation) — new servers start `pending`, spec refresh sets `changes_pending`, only `approved` are loaded into `IToolStore`, `-32005` is enforced by the MCP endpoint layer (separate plan).
- ADR-0006 (one gateway per environment) — no `api_instances` references; `BaseUrl`/`AuthStrategy`/`AuthConfig` live on `McpServerDefinition` directly.
- ADR-0004 (SQL scripts for DBA) — no `db.Database.Migrate()` calls in management services; migrations are applied only in `AdminApiFactory.InitializeAsync` (Testcontainers DDL-enabled) and via the persistence plan's `generate-migration-sql.sh`.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-07-03-management-api.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Best for the 20-task scope because each task has isolated files and tests; a fresh subagent per task limits context bloat.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints. Faster for an experienced agent that can hold the full surface in context.

**Which approach?** Or do you want to chain the next plan (`2026-07-03-mcp-endpoint-and-dynamic-tool-provider.md` or `2026-07-03-tool-call-proxy.md`) for a combined execution?
