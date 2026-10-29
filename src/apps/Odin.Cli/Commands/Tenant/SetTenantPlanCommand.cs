using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Set tenant hosting plan, and optionally its quota and public web presence (docs/hosting-plans.md)")]
public sealed class SetTenantPlanCommand : AsyncCommand<SetTenantPlanCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [Description("Tenant domain")]
        [CommandArgument(0, "<tenant>")]
        public string TenantDomain { get; init; } = "";

        [Description("Plan id, as the hosting provider names it")]
        [CommandArgument(1, "<plan>")]
        public string PlanId { get; init; } = "";

        [Description("Bytes the identity may store; left out, unchanged")]
        [CommandOption("--quota-bytes <BYTES>")]
        public long? QuotaBytes { get; init; }

        [Description("Whether the identity has a public home page (true or false); left out, unchanged")]
        [CommandOption("--public-web-presence <BOOL>")]
        public bool? PublicWebPresence { get; init; }

        public override ValidationResult Validate() => QuotaBytes is <= 0
            ? ValidationResult.Error("--quota-bytes must be more than 0")
            : base.Validate();
    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        var result = await TenantStatusApi.SetPlanAsync(httpClient, settings.TenantDomain, settings.PlanId,
            settings.QuotaBytes, settings.PublicWebPresence);
        AnsiConsole.MarkupLineInterpolated(
            $"{settings.TenantDomain}: plan {settings.PlanId.Trim()}, quota {settings.QuotaBytes?.ToString() ?? "unchanged"}, public web presence {settings.PublicWebPresence?.ToString() ?? "unchanged"} (was {result.PreviousPlanId ?? "none"}, {result.PreviousQuotaBytes?.ToString() ?? "default"}, {result.PreviousPublicWebPresence?.ToString() ?? "?"})");
        return 0;
    }
}
