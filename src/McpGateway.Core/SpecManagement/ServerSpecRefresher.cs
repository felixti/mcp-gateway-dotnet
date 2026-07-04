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
