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
        result.Errors.Should().NotBeEmpty();
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
