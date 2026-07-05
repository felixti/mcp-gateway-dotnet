using EFCore.NamingConventions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace McpGateway.Persistence;

public class McpGatewayDbContext : DbContext
{
    public const string SchemaName = "ai_gateway";

    private static readonly ValueConverter<ToolMode, string> ToolModeConverter = new(
        v => v.ToString().ToLowerInvariant(),
        v => (ToolMode)Enum.Parse(typeof(ToolMode), v, ignoreCase: true));

    private static readonly ValueConverter<ClientProfile, string> ClientProfileConverter = new(
        v => v.ToString().ToLowerInvariant(),
        v => (ClientProfile)Enum.Parse(typeof(ClientProfile), v, ignoreCase: true));

    private static readonly ValueConverter<SourceType, string> SourceTypeConverter = new(
        v => v.ToCanonicalString(),
        v => ParseSourceType(v));

    private static SourceType ParseSourceType(string value) => value switch
    {
        "openapi" => SourceType.OpenApi,
        "mcp-upstream" => SourceType.McpUpstream,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown source type")
    };

    public McpGatewayDbContext(DbContextOptions<McpGatewayDbContext> options)
        : base(options)
    {
    }

    public DbSet<McpServerDefinitionEntity> ServerDefinitions => Set<McpServerDefinitionEntity>();
    public DbSet<ToolEntity> Tools => Set<ToolEntity>();
    public DbSet<ToolOverrideEntity> ToolOverrides => Set<ToolOverrideEntity>();
    public DbSet<GatewayApiKeyEntity> GatewayApiKeys => Set<GatewayApiKeyEntity>();
    public DbSet<SpecVersionEntity> SpecVersions => Set<SpecVersionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<McpServerDefinitionEntity>(entity =>
        {
            entity.ToTable("mcp_server_defs");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.ApprovalStatus)
                  .HasFilter("approval_status = 'approved'");
            entity.Property(e => e.AuthConfig).HasDefaultValue("{}");
            entity.Property(e => e.ToolMode)
                  .HasConversion(ToolModeConverter)
                  .HasDefaultValue(ToolMode.All);
            entity.Property(e => e.ClientProfile)
                  .HasConversion(ClientProfileConverter)
                  .HasDefaultValue(ClientProfile.Universal);
            entity.Property(e => e.SourceType)
                  .HasConversion(SourceTypeConverter)
                  .HasMaxLength(32)
                  .HasDefaultValue(SourceType.OpenApi);
            entity.Property(e => e.PollIntervalMinutes).HasDefaultValue(1440);
            entity.Property(e => e.Status).HasDefaultValue("active");
            entity.Property(e => e.ApprovalStatus).HasDefaultValue("pending");
        });

        modelBuilder.Entity<ToolEntity>(entity =>
        {
            entity.ToTable("tools");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ServerDefinitionId);
            entity.HasIndex(e => new { e.ServerDefinitionId, e.ToolName }).IsUnique();
            entity.Property(e => e.AuthConfig).HasDefaultValue("{}");
            entity.Property(e => e.Visible).HasDefaultValue(true);
            entity.Property(e => e.HttpMethod).IsRequired(false);
            entity.Property(e => e.HttpPath).IsRequired(false);
            entity.HasOne(e => e.ServerDefinition)
                  .WithMany(s => s.Tools)
                  .HasForeignKey(e => e.ServerDefinitionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ToolOverrideEntity>(entity =>
        {
            entity.ToTable("tool_overrides");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ServerDefinitionId, e.ToolName }).IsUnique();
            entity.Property(e => e.Visible).HasDefaultValue(true);
            entity.HasOne(e => e.ServerDefinition)
                  .WithMany(s => s.ToolOverrides)
                  .HasForeignKey(e => e.ServerDefinitionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GatewayApiKeyEntity>(entity =>
        {
            entity.ToTable("gateway_api_keys");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.KeyPrefix)
                  .HasFilter("revoked_at IS NULL");
            entity.HasOne(e => e.ServerDefinition)
                  .WithMany(s => s.GatewayApiKeys)
                  .HasForeignKey(e => e.ServerDefinitionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpecVersionEntity>(entity =>
        {
            entity.ToTable("spec_versions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ServerDefinitionId, e.CreatedAt });
            entity.HasOne(e => e.ServerDefinition)
                  .WithMany(s => s.SpecVersions)
                  .HasForeignKey(e => e.ServerDefinitionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
