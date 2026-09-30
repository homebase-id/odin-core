using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Odin.Services.Registry;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Resume a paused or out-of-quota tenant (use 'enable' for a disabled one)")]
public sealed class ResumeTenantCommand : AsyncCommand<ResumeTenantCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [Description("Tenant domain")]
        [CommandArgument(0, "<tenant>")]
        public string TenantDomain { get; init; } = "";
    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);

        // Resuming is for temporary holds. Re-enabling a disabled tenant is a deliberate, separate step.
        var tenant = await TenantStatusApi.GetTenantAsync(httpClient, settings.TenantDomain);
        if (tenant.Status == TenantStatus.Disabled)
        {
            var how = TenantStatusRules.HasMovedAway(tenant.Status, tenant.DisabledReason)
                ? "it moved to another host: 'tenant unlock-moved' rolls the move back (agents/identity-move/README.md)"
                : $"'tenant set-status {settings.TenantDomain} active' re-enables it";
            throw new Exception(
                $"Tenant {settings.TenantDomain} is {TenantStatusApi.Describe(tenant.Status, tenant.DisabledReason)}; {how}");
        }

        var previous = await TenantStatusApi.SetStatusAsync(httpClient, settings.TenantDomain, TenantStatus.Active);
        TenantStatusApi.WriteChange(settings.TenantDomain, previous, TenantStatus.Active, null);
        return 0;
    }
}
