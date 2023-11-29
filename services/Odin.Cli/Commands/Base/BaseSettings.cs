using System.ComponentModel;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Base;

public class BaseSettings : CommandSettings
{
    [Description("Verbose output")]
    [CommandOption("-V | --verbose")]
    public bool Verbose { get; set; } = false;

    [Description("Do not show headers")]
    [CommandOption("-N | --no-headers")]
    public bool NoHeaders { get; set; } = false;
}

