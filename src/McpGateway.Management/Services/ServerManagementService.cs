using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using McpGateway.Core.McpUpstream;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Auth;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using Microsoft.Extensions.Logging;

namespace McpGateway.Management.Services;

public class ServerManagementService
{
    private readonly IServerDefinitionRepository _serverRepo;
    private readonly ISpecVersionRepository _specVersionRepo;
    private readonly IToolStore _toolStore;
    private readonly IToolGenerator _toolGenerator;
    private readonly IOpenApiSpecValidator _specValidator;
    private readonly ISpecFetcher _specFetcher;
    private readonly ISpecDiffService _diffService;
    private readonly ICallerIdentityAccessor _caller;
    private readonly IMcpUpstreamClient _upstreamClient;
    private readonly UpstreamCatalogImporter _catalogImporter;
    private readonly ILogger<ServerManagementService> _logger;

    public ServerManagementService(
        IServerDefinitionRepository serverRepo,
        ISpecVersionRepository specVersionRepo,
        IToolStore toolStore,
        IToolGenerator toolGenerator,
        IOpenApiSpecValidator specValidator,
        ISpecFetcher specFetcher,
        ISpecDiffService diffService,
        ICallerIdentityAccessor caller,
        IMcpUpstreamClient upstreamClient,
        UpstreamCatalogImporter catalogImporter,
        ILogger<ServerManagementService> logger)
    {
        _serverRepo = serverRepo;
        _specVersionRepo = specVersionRepo;
        _toolStore = toolStore;
        _toolGenerator = toolGenerator;
        _specValidator = specValidator;
        _specFetcher = specFetcher;
        _diffService = diffService;
        _caller = caller;
        _upstreamClient = upstreamClient;
        _catalogImporter = catalogImporter;
        _logger = logger;
    }

    public async Task<ServerResponse> RegisterAsync(CreateServerRequest request, CancellationToken ct)
    {
        var existing = await _serverRepo.GetByNameAsync(request.Name, ct);
        if (existing is not null)
            throw new ConflictException($"Server definition '{request.Name}' already exists.");

        var sourceType = ParseSourceType(request.SourceType);

        IReadOnlyList<SpecValidationIssue>? warnings = null;
        var definition = sourceType == SourceType.McpUpstream
            ? await RegisterMcpUpstreamAsync(request, ct)
            : await RegisterOpenApiAsync(request, ct, w => warnings = w);

        var saved = await _serverRepo.AddAsync(definition, ct);
        return ServerResponse.FromDomain(saved, warnings);
    }

    private async Task<McpServerDefinition> RegisterOpenApiAsync(CreateServerRequest request, CancellationToken ct, Action<IReadOnlyList<SpecValidationIssue>> setWarnings)
    {
        var (specContent, specHash) = !string.IsNullOrWhiteSpace(request.SpecContent)
            ? (request.SpecContent!, ComputeHash(request.SpecContent!))
            : await FetchSpecAsync(request.SpecSourceUrl!, ct);

        var clientProfile = Enum.Parse<ClientProfile>(Capitalize(request.ClientProfile));
        ValidateAndThrow(specContent, clientProfile);
        setWarnings(GetWarnings(specContent, clientProfile));

        var parser = new OpenApiParser();
        var document = parser.Parse(specContent);

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
            SourceType = SourceType.OpenApi,
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

        return definition;
    }

    private async Task<McpServerDefinition> RegisterMcpUpstreamAsync(CreateServerRequest request, CancellationToken ct)
    {
        var upstreamUrl = request.UpstreamUrl?.Trim();
        if (string.IsNullOrWhiteSpace(upstreamUrl))
            throw new ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.UpstreamUrl), "UpstreamUrl is required for mcp-upstream source type.")
            });

        var upstreamTools = await _upstreamClient.ListToolsAsync(upstreamUrl, ct);
        var now = DateTime.UtcNow;
        var definition = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            SpecSourceUrl = upstreamUrl,
            SpecContent = "{}",
            SpecHash = ComputeHash("{}"),
            BaseUrl = upstreamUrl.TrimEnd('/'),
            AuthStrategy = request.AuthStrategy,
            AuthConfig = request.AuthConfig.ToJsonString(),
            ToolMode = ToolMode.All,
            ClientProfile = ClientProfile.Universal,
            SourceType = SourceType.McpUpstream,
            PollIntervalMinutes = request.PollIntervalMinutes,
            Status = "active",
            ApprovalStatus = "pending",
            CreatedAt = now,
            UpdatedAt = now
        };
        definition.Tools = _catalogImporter.Import(upstreamTools, definition.Id).ToList();

        return definition;
    }

    public async Task<SpecValidationReport> ValidateAsync(CreateServerRequest request, CancellationToken ct)
    {
        var specContent = !string.IsNullOrWhiteSpace(request.SpecContent)
            ? request.SpecContent!
            : (await FetchSpecAsync(request.SpecSourceUrl!, ct)).Content;

        var clientProfile = Enum.Parse<ClientProfile>(Capitalize(request.ClientProfile));
        return _specValidator.Validate(specContent, clientProfile);
    }

    public async Task<ServerResponse> GetAsync(string name, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);
        var warnings = GetWarnings(def.SpecContent, def.ClientProfile);
        return ServerResponse.FromDomain(def, warnings);
    }

    public async Task<IReadOnlyList<ServerResponse>> ListAsync(CancellationToken ct)
    {
        var defs = await _serverRepo.ListAsync(ct);
        return defs
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => ServerResponse.FromDomain(d, GetWarnings(d.SpecContent, d.ClientProfile)))
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
            var unchangedWarnings = GetWarnings(def.SpecContent, def.ClientProfile);
            return new RefreshResponse(def.Id, def.ApprovalStatus, false, newHash, def.LastRefreshedAt.Value, def.Tools.Count, unchangedWarnings);
        }

        ValidateAndThrow(newContent, def.ClientProfile);
        var warnings = GetWarnings(newContent, def.ClientProfile);

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

        return new RefreshResponse(def.Id, def.ApprovalStatus, true, newHash, def.LastRefreshedAt.Value, newTools.Count, warnings);
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

    public async Task<ServerResponse> UploadSpecAsync(string name, SpecUploadRequest request, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(name, ct)
            ?? throw new NotFoundException("Server definition", name);

        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.Content), "Spec content is empty.")
            });

        var newHash = ComputeHash(request.Content);
        if (string.Equals(newHash, def.SpecHash, StringComparison.Ordinal))
        {
            def.LastRefreshedAt = DateTime.UtcNow;
            def.UpdatedAt = def.LastRefreshedAt.Value;
            await _serverRepo.UpdateAsync(def, ct);
            var unchangedWarnings = GetWarnings(def.SpecContent, def.ClientProfile);
            return ServerResponse.FromDomain(def, unchangedWarnings);
        }

        ValidateAndThrow(request.Content, def.ClientProfile);
        var warnings = GetWarnings(request.Content, def.ClientProfile);

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
        def.SpecHash = newHash;
        def.ApprovalStatus = "changes_pending";
        def.ApprovedAt = null;
        def.ApprovedBy = null;
        def.UpdatedAt = DateTime.UtcNow;

        await _serverRepo.UpdateToolsAsync(def.Id, newTools, ct);
        await _serverRepo.UpdateAsync(def, ct);
        _toolStore.RemoveServer(name);

        return ServerResponse.FromDomain(def, warnings);
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
        return (def.SpecContent, ComputeHash(def.SpecContent));
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

    private static SourceType ParseSourceType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SourceType.OpenApi;

        return value.ToLowerInvariant() switch
        {
            "openapi" => SourceType.OpenApi,
            "mcp-upstream" => SourceType.McpUpstream,
            _ => throw new ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(CreateServerRequest.SourceType),
                    $"Invalid source type '{value}'. Supported values are 'openapi' and 'mcp-upstream'.")
            })
        };
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private IReadOnlyList<SpecValidationIssue> GetWarnings(string specContent, ClientProfile profile)
    {
        var report = _specValidator.Validate(specContent, profile);
        return report.Warnings;
    }

    private void ValidateAndThrow(string specContent, ClientProfile profile)
    {
        var report = _specValidator.Validate(specContent, profile);
        if (report.Errors.Count > 0)
        {
            throw new OpenApiSpecValidationException(report);
        }
    }

    private async Task<(string Content, string Hash)> FetchSpecAsync(string url, CancellationToken ct)
    {
        var fetched = await _specFetcher.FetchAsync(SpecSource.FromUrl(url), ct);
        return (fetched.Content, fetched.Hash);
    }
}
