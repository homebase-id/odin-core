using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Base;

public class ApiSettings : BaseSettings
{
    [Description("API key")]
    [CommandOption("-k|--key <VALUE>")]
    public string ApiKey { get; init; } = "";

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return ValidationResult.Error("API key is required");
        }
        return base.Validate();
    }
}