using System.ComponentModel;
using Odin.Cli.Commands.Base;
using Odin.Cli.Commands.Tenants;
using Odin.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Show tenant")]
public sealed class ShowTenantCommand : BaseCommand<ShowTenantCommand.Settings>
{
    public sealed class Settings : TenantSettings
    {
        [Description("Include payload sizes (slow)")]
        [CommandOption("-p|--payload")]
        public bool IncludePayload { get; set; } = false;
    }

    //

    private readonly ITenantFileSystem _tenantFileSystem;

    public ShowTenantCommand(ITenantFileSystem tenantFileSystem)
    {
        _tenantFileSystem = tenantFileSystem;
    }

    //

    protected override int Run(CommandContext context, Settings settings)
    {
        var tenant = _tenantFileSystem.Load(
            settings.TenantIdOrDomain,
            settings.IncludePayload,
            settings.Verbose);

        var grid = new Grid();
        grid.AddColumn(); // Domain
        grid.AddColumn(); // Id
        grid.AddColumn(); // Registration Size
        grid.AddColumn(); // Payload Size
        grid.AddRow(
            new Text("Domain", new Style(Color.Green)).LeftJustified(),
            new Text("Id", new Style(Color.Green)).LeftJustified(),
            new Text("Reg. Size", new Style(Color.Green)).RightJustified(),
            new Text("Payload Size", new Style(Color.Green)).RightJustified());

        var payLoadSize = settings.IncludePayload ? HumanReadableBytes(tenant.PayloadSize) : "-";
        grid.AddRow(
            new Text(tenant.Registration.PrimaryDomainName).LeftJustified(),
            new Text(tenant.Registration.Id.ToString()).LeftJustified(),
            new Text(HumanReadableBytes(tenant.RegistrationSize)).RightJustified(),
            new Text(payLoadSize).RightJustified());

        AnsiConsole.Write(grid);

        return 0;
    }
}