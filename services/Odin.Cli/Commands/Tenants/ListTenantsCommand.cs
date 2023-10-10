using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Odin.Cli.Commands.Base;
using Odin.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace Odin.Cli.Commands.Tenants;

[Description("List all tenants in root directory")]
public sealed class ListTenantsCommand : ApiCommand<ListTenantsCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        // [SuppressMessage("ReSharper", "InconsistentNaming")]
        // public enum OutputType
        // {
        //     grid,
        //     tree,
        // }
        //
        // [Description("Include payload sizes (slow)")]
        // [CommandOption("-p|--payload")]
        // public bool IncludePayload { get; set; } = false;
        //
        // [Description("Only show domains")]
        // [CommandOption("-q|--quiet")]
        // public bool OnlyShowDomains { get; set; } = false;
        //
        // [Description("Output type:\n'grid': show as grid (default)\n'tree': show as tree")]
        // [CommandOption("-o|--output")]
        // public OutputType Output { get; set; } = OutputType.grid;
    }

    //

    public ListTenantsCommand()
    {

    }

    //

    protected override int Run(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine($"[green]KEY:[/] {settings.ApiKey}");
        return 0;
    }

    //


}


