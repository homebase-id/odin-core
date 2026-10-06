using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Odin.Services.Registry;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Pause tenant: callers are told to retry later and its background services stop")]
public sealed class PauseTenantCommand : AsyncCommand<PauseTenantCommand.Settings>
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
        var previous = await TenantStatusApi.SetStatusAsync(httpClient, settings.TenantDomain, TenantStatus.Paused);
        TenantStatusApi.WriteChange(settings.TenantDomain, previous, TenantStatus.Paused, null);
        return 0;
    }
}
