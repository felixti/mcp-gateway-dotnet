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
