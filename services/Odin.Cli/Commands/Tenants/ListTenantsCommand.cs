using System.ComponentModel;
using Odin.Cli.Services;
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

    private readonly ITenantFileSystem _tenantFileSystem;

    public ListTenantsCommand(ITenantFileSystem tenantFileSystem)
    {
        _tenantFileSystem = tenantFileSystem;
    }

    //

    protected override int Run(CommandContext context, Settings settings)
    {
        _tenantFileSystem.Verbose = settings.Verbose;
        var tenants = _tenantFileSystem.LoadAll(settings.TenantRootDir);

        var root = new Tree("[yellow]Tenants[/]");
        foreach (var tenant in tenants)
        {
            var t = root.AddNode($"[yellow]{tenant.Registration.PrimaryDomainName}[/]");
            t.AddNode($"[yellow]Id:[/] {tenant.Registration.Id}");
            t.AddNode($"[yellow]Registration Size:[/] {HumanReadableBytes(tenant.RegistrationSize)}");

            var p = t.AddNode($"[yellow]Payload Size:[/] {HumanReadableBytes(tenant.PayloadSize)}");
            foreach (var payload in tenant.Payloads)
            {
                var s = p.AddNode($"[yellow]{payload.Shard}[/]");
                s.AddNode($"[yellow]Size:[/] {HumanReadableBytes(payload.Size)}");
            }
        }

        AnsiConsole.Write(root);

        return 0;
    }
}
