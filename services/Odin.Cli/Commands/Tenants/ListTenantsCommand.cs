using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Odin.Cli.Commands.Base;
using Odin.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace Odin.Cli.Commands.Tenants;

[Description("List all tenants in root directory")]
public sealed class ListTenantsCommand : BaseCommand<ListTenantsCommand.Settings>
{
    public sealed class Settings : TenantsSettings
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public enum OutputType
        {
            grid,
            tree,
        }

        [Description("Include payload sizes (slow)")]
        [CommandOption("-p|--payload")]
        public bool IncludePayload { get; set; } = false;

        [Description("Only show domains")]
        [CommandOption("-q|--quiet")]
        public bool OnlyShowDomains { get; set; } = false;

        [Description("Output type:\n'grid': show as grid (default)\n'tree': show as tree")]
        [CommandOption("-o|--output")]
        public OutputType Output { get; set; } = OutputType.grid;
    }

    //

    private readonly ITenantFileSystem _tenantFileSystem;

    public ListTenantsCommand(ITenantFileSystem tenantFileSystem)
    {
        _tenantFileSystem = tenantFileSystem;
    }

    //

    protected override int Run(CommandContext context, Settings settings)
    {
        if (settings.OnlyShowDomains)
        {
            return Grid(context, settings);
        }
        if (settings.Output == Settings.OutputType.tree)
        {
            return Tree(context, settings);
        }
        return Grid(context, settings);
    }

    //

    private int Grid(CommandContext context, Settings settings)
    {
        var tenants = _tenantFileSystem.LoadAll(
            settings.TenantRootDir,
            settings.IncludePayload,
            settings.Verbose);

        var grid = new Grid();
        if (settings.OnlyShowDomains)
        {
            grid.AddColumn(); // Domain
        }
        else
        {
            grid.AddColumn(); // Domain
            grid.AddColumn(); // Id
            grid.AddColumn(); // Registration Size
            grid.AddColumn(); // Payload Size
            grid.AddRow(
                new Text("Domain", new Style(Color.Green)).LeftJustified(),
                new Text("Id", new Style(Color.Green)).LeftJustified(),
                new Text("Reg. Size", new Style(Color.Green)).RightJustified(),
                new Text("Payload Size", new Style(Color.Green)).RightJustified());
        }

        foreach (var tenant in tenants)
        {
            if (settings.OnlyShowDomains)
            {
                grid.AddRow(new Text(tenant.Registration.PrimaryDomainName));
            }
            else
            {
                var payLoadSize = settings.IncludePayload ? HumanReadableBytes(tenant.PayloadSize) : "-";
                grid.AddRow(
                    new Text(tenant.Registration.PrimaryDomainName).LeftJustified(),
                    new Text(tenant.Registration.Id.ToString()).LeftJustified(),
                    new Text(HumanReadableBytes(tenant.RegistrationSize)).RightJustified(),
                    new Text(payLoadSize).RightJustified());
            }
        }
        AnsiConsole.Write(grid);

        return 0;
    }


    //

    private int Tree(CommandContext context, Settings settings)
    {
        var tenants = _tenantFileSystem.LoadAll(
            settings.TenantRootDir,
            settings.IncludePayload,
            settings.Verbose);

        var root = new Tree("[yellow]Tenants[/]");
        foreach (var tenant in tenants)
        {
            var t = root.AddNode($"[yellow]{tenant.Registration.PrimaryDomainName}[/]");
            t.AddNode($"[yellow]Id:[/] {tenant.Registration.Id}");
            t.AddNode($"[yellow]Registration Size:[/] {HumanReadableBytes(tenant.RegistrationSize)}");

            if (settings.IncludePayload)
            {
                var p = t.AddNode($"[yellow]Payload Size:[/] {HumanReadableBytes(tenant.PayloadSize)}");
                foreach (var payload in tenant.Payloads)
                {
                    var s = p.AddNode($"[yellow]{payload.Shard}[/]");
                    s.AddNode($"[yellow]Size:[/] {HumanReadableBytes(payload.Size)}");
                }
            }
        }

        AnsiConsole.Write(root);

        return 0;
    }

}


