using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Odin.Services.Registry;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Resume a paused or out-of-quota tenant")]
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

        // Resuming is for temporary holds: the server refuses a disabled tenant, and says how to re-enable it
        var previous = await TenantStatusApi.ResumeAsync(httpClient, settings.TenantDomain);
        TenantStatusApi.WriteChange(settings.TenantDomain, previous, TenantStatus.Active, null);
        return 0;
    }
}
