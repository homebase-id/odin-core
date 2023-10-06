using System.ComponentModel;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Base;

public class BaseSettings : CommandSettings
{
    // [Description("Path to config file. 'appsettings.production.json' if not specified.")]
    // [CommandOption("-c | --config <PATH>")]
    // public string? Config { get; set; }

    [Description("Verbose output")]
    [CommandOption("-V | --verbose")]
    public bool Verbose { get; set; } = false;
}

