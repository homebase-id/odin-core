using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Base;

public class ApiSettings : BaseSettings
{
    [Description("Identity host(:port) (alt: set env var ODIN_ADMIN_IDENTITY_HOST)")]
    [CommandOption("-I|--identity-host <VALUE>")]
    public string IdentityHost { get; set; } = "";

    [Description("API key value (alt: set env var ODIN_ADMIN_API_KEY)")]
    [CommandOption("-K|--api-key <VALUE>")]
    public string ApiKey { get; set; } = "";

    [Description("API key header value (default: Odin-Admin-Api-Key)")]
    [CommandOption("--api-key-header <VALUE>")]
    public string ApiKeyHeader { get; init; } = "Odin-Admin-Api-Key";

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(IdentityHost))
        {
            var envHost = Environment.GetEnvironmentVariable("ODIN_ADMIN_IDENTITY_HOST") ?? "";
            if (envHost != "")
            {
                IdentityHost = envHost;
            }
            else
            {
                return ValidationResult.Error("Identity host (-I) is required");
            }
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            var envApiKey = Environment.GetEnvironmentVariable("ODIN_ADMIN_API_KEY") ?? "";
            if (envApiKey != "")
            {
                ApiKey = envApiKey;
            }
            else
            {
                return ValidationResult.Error("API key (-K) is required");
            }
        }
        return base.Validate();
    }
}