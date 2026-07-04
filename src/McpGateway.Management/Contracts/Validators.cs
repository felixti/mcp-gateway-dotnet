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

public class SpecSourceUpdateRequestValidator : AbstractValidator<SpecSourceUpdateRequest>
{
    public SpecSourceUpdateRequestValidator()
    {
        RuleFor(r => r.SpecSourceUrl)
            .NotEmpty()
            .Must(BeAValidAbsoluteHttpUrl)
            .WithMessage("SpecSourceUrl must be an absolute http or https URL.");
    }

    private static bool BeAValidAbsoluteHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
