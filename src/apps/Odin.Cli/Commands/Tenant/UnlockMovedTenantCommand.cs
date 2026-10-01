using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Odin.Services.Registry;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Roll a move back: take a copy disabled as moved to paused. Only a moved copy, and only to paused")]
public sealed class UnlockMovedTenantCommand : AsyncCommand<UnlockMovedTenantCommand.Settings>
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
        var previous = await TenantStatusApi.UnlockMovedAsync(httpClient, settings.TenantDomain);
        TenantStatusApi.WriteChange(settings.TenantDomain, previous, TenantStatus.Paused, null);

        // This host cannot see the copy it moved to; the runbook says what has to happen before resuming this one
        AnsiConsole.MarkupLine("[yellow]This copy is as it was at export. Before resuming it, follow \"Rolling back a move\" " +
                               "in agents/identity-move/README.md[/]");
        return 0;
    }
}
