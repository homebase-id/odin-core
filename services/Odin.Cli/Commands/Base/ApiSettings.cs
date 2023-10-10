using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Base;

public class ApiSettings : BaseSettings
{
    [Description("Identity host URL")]
    [CommandArgument(0, "<identity-host>")]
    public string IdentityHost { get; init; } = "";

    [Description("API key value")]
    [CommandOption("--api-key <VALUE>")]
    public string ApiKey { get; init; } = "";

    [Description("API key header value (default: Odin-Admin-Api-Key)")]
    [CommandOption("--api-key-header <VALUE>")]
    public string ApiKeyHeader { get; init; } = "Odin-Admin-Api-Key";

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return ValidationResult.Error("API key is required");
        }
        return base.Validate();
    }
}