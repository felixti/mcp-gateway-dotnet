using System.Text.Json;
using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.SpecManagement;

public class SpecDiffServiceTests
{
    private readonly SpecDiffService _service = new(new ToolGenerator());

    private const string SpecV1 = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Test", "version": "1.0" },
      "paths": {
        "/users": {
          "get": {
            "operationId": "listUsers",
            "summary": "List users",
            "responses": { "200": { "description": "OK" } }
          }
        }
      }
    }
    """;

    private const string SpecV2 = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Test", "version": "2.0" },
      "paths": {
        "/users": {
          "get": {
            "operationId": "listUsers",
            "summary": "List users (updated)",
            "responses": { "200": { "description": "OK" } }
          }
        },
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

    private const string SpecV1Renamed = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Test", "version": "1.0" },
      "paths": {
        "/users": {
          "delete": {
            "operationId": "deleteUser",
            "summary": "Delete a user",
            "responses": { "204": { "description": "No content" } }
          }
        }
      }
    }
    """;

    [Fact]
    public void Diff_IdenticalSpecs_ReturnsNoChanges()
    {
        var result = _service.Diff(SpecV1, SpecV1, ClientProfile.Universal);

        result.HasChanges.Should().BeFalse();
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AddedEndpoint_AppearsInAdded()
    {
        var result = _service.Diff(SpecV1, SpecV2, ClientProfile.Universal);

        result.Added.Should().Contain("listinvoices");
    }

    [Fact]
    public void Diff_RemovedEndpoint_AppearsInRemoved()
    {
        var result = _service.Diff(SpecV1, SpecV1Renamed, ClientProfile.Universal);

        result.Removed.Should().Contain("listusers");
        result.Added.Should().Contain("deleteuser");
    }

    [Fact]
    public void Diff_ChangedDescription_AppearsInChanged()
    {
        var result = _service.Diff(SpecV1, SpecV2, ClientProfile.Universal);

        result.Changed.Should().ContainSingle(c => c.ToolName == "listusers");
        result.Changed.First(c => c.ToolName == "listusers").ChangedFields.Should().Contain("description");
    }

    [Fact]
    public void Diff_JsonOutput_ContainsAllSections()
    {
        var result = _service.Diff(SpecV1, SpecV2, ClientProfile.Universal);

        var json = result.ToJson();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("added").GetArrayLength().Should().BeGreaterThan(0);
        doc.RootElement.GetProperty("changed").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Diff_EmptySpecs_ReturnEmptyResult()
    {
        const string empty = """{"openapi":"3.0.0","info":{"title":"E","version":"1.0"},"paths":{}}""";

        var result = _service.Diff(empty, empty, ClientProfile.Universal);

        result.HasChanges.Should().BeFalse();
    }
}
