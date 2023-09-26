using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenants;

[Description("List tenants.")]
public sealed class ListTenantsCommand : BaseCommand<ListTenantsCommand.Settings>
{
    public sealed class Settings : TenantsSettings
    {
    }

    //

    protected override int Run(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine("[underline default]Tenants[/]");
        return 0;
    }
}
