using EFCore.NamingConventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace McpGateway.Persistence;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<McpGatewayDbContext>
{
    public McpGatewayDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("PostgreSql")
            ?? "Host=localhost;Database=mcp_gateway;Username=mcp;Password=mcp";

        var optionsBuilder = new DbContextOptionsBuilder<McpGatewayDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();
        return new McpGatewayDbContext(optionsBuilder.Options);
    }
}
