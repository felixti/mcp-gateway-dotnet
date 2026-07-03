# Multi-stage build for MCP Gateway .NET 10
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (layer caching)
COPY McpGateway.sln ./
COPY src/McpGateway.Api/McpGateway.Api.csproj src/McpGateway.Api/
COPY src/McpGateway.Core/McpGateway.Core.csproj src/McpGateway.Core/
COPY src/McpGateway.Persistence/McpGateway.Persistence.csproj src/McpGateway.Persistence/
COPY src/McpGateway.McpSdk/McpGateway.McpSdk.csproj src/McpGateway.McpSdk/
COPY src/McpGateway.Management/McpGateway.Management.csproj src/McpGateway.Management/
COPY src/McpGateway.Telemetry/McpGateway.Telemetry.csproj src/McpGateway.Telemetry/
RUN dotnet restore

# Copy source and build
COPY . .
RUN dotnet publish src/McpGateway.Api/McpGateway.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Non-root user
USER app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "McpGateway.Api.dll"]