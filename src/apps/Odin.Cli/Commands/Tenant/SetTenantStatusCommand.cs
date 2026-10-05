using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Odin.Cli.Commands.Base;
using Odin.Cli.Factories;
using Odin.Services.Registry;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Odin.Cli.Commands.Tenant;

[Description("Set tenant status (active, out-of-quota, paused, disabled)")]
public sealed class SetTenantStatusCommand : AsyncCommand<SetTenantStatusCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [Description("Tenant domain")]
        [CommandArgument(0, "<tenant>")]
        public string TenantDomain { get; init; } = "";

        [Description("Status: active, out-of-quota, paused or disabled")]
        [CommandArgument(1, "<status>")]
        public string Status { get; init; } = "";

        [Description("Disabled reason: admin (default), pending-deletion or moved. Only valid with disabled")]
        [CommandOption("-r|--reason <REASON>")]
        public string? Reason { get; init; }

        public override ValidationResult Validate()
        {
            if (!TenantStatusRules.TryParse<TenantStatus>(Status, out _))
            {
                return ValidationResult.Error($"Unknown status '{Status}'");
            }
            if (Reason != null && !TenantStatusRules.TryParse<DisabledReason>(Reason, out _))
            {
                return ValidationResult.Error($"Unknown reason '{Reason}'");
            }
            return base.Validate();
        }
    }

    //

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        TenantStatusRules.TryParse<TenantStatus>(settings.Status, out var status);
        DisabledReason? reason = null;
        if (settings.Reason != null && TenantStatusRules.TryParse<DisabledReason>(settings.Reason, out var parsedReason))
        {
            reason = parsedReason;
        }

        var httpClient = CliHttpClientFactory.Create(settings.IdentityHost, settings.ApiKeyHeader, settings.ApiKey);
        var previous = await TenantStatusApi.SetStatusAsync(httpClient, settings.TenantDomain, status, reason);
        TenantStatusApi.WriteChange(settings.TenantDomain, previous, status,
            TenantStatusRules.NormalizeReason(status, reason));
        return 0;
    }
}
