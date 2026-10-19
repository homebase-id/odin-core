using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Odin.Core.Serialization;
using Odin.Services.Admin.Tenants;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Set tenant hosting plan, and so its disk quota (docs/hosting-plans.md)")]
public sealed class SetTenantPlanCommand : AsyncCommand<SetTenantPlanCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [Description("Tenant domain")]
        [CommandArgument(0, "<tenant>")]
        public string TenantDomain { get; init; } = "";

        [Description("Plan id, one of the host's Registry:Plans")]
        [CommandArgument(1, "<plan>")]
        public string PlanId { get; init; } = "";
    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        var body = OdinSystemSerializer.Serialize(new SetTenantPlanRequest { PlanId = settings.PlanId });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await httpClient.PatchAsync($"tenants/{settings.TenantDomain}/plan", content);
        await TenantStatusApi.EnsureSuccessAsync(response, settings.TenantDomain);

        var result = OdinSystemSerializer.Deserialize<SetTenantPlanResponse>(await response.Content.ReadAsStringAsync());
        AnsiConsole.MarkupLineInterpolated(
            $"{settings.TenantDomain}: plan {settings.PlanId.Trim()} (was {result?.PreviousPlanId ?? "none"})");
        return 0;
    }
}
