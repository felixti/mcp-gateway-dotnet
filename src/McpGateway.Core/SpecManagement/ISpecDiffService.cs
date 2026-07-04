using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.SpecManagement;

public interface ISpecDiffService
{
    SpecDiffResult Diff(
        string oldSpecContent,
        string newSpecContent,
        ClientProfile profile);
}
