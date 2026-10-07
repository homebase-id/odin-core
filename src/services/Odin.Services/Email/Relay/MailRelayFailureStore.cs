using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;

#nullable enable

namespace Odin.Services.Email.Relay;

/// <summary>
/// Why the relay last refused a domain, when it did.
///
/// Onboarding runs in a job, and a job's error lives on its row - which has no identity, is
/// read by nothing, and is purged a day after the job gives up. On 2026-10-07 that was the only
/// trace of a tenant whose mail could not leave, while every health surface said all was well.
/// This is the trace the health check and the Email tab can actually read.
/// </summary>
public interface IMailRelayFailureStore
{
    Task<MailRelayFailure?> GetAsync(string domain);
    Task RecordAsync(string domain, Exception exception);
    Task ClearAsync(string domain);
}

public class MailRelayFailure
{
    /// <summary>The relay's own words - what the owner needs to see, e.g. the plan-limit refusal.</summary>
    public string Message { get; init; } = "";

    public int? StatusCode { get; init; }

    /// <summary>True when retrying cannot help until someone changes something.</summary>
    public bool Permanent { get; init; }

    public UnixTimeUtc At { get; init; }
}

/// <summary>
/// System settings table, not per-tenant storage: the onboarding job runs in system scope
/// with no tenant database, and the system database is shared by every node, so the failure
/// recorded by one node's job is what another node's health check reads.
/// </summary>
public class MailRelayFailureStore(IServiceProvider serviceProvider) : IMailRelayFailureStore
{
    private static string Key(string domain) => $"mail-relay-failure:{domain.ToLowerInvariant()}";

    public async Task<MailRelayFailure?> GetAsync(string domain)
    {
        using var scope = serviceProvider.CreateScope();
        var table = scope.ServiceProvider.GetRequiredService<TableSettings>();

        var record = await table.GetAsync(Key(domain));
        return record == null || string.IsNullOrEmpty(record.value)
            ? null
            : OdinSystemSerializer.Deserialize<MailRelayFailure>(record.value);
    }

    public async Task RecordAsync(string domain, Exception exception)
    {
        var relayException = exception as MailRelayException;
        var failure = new MailRelayFailure
        {
            Message = exception.Message,
            StatusCode = relayException?.StatusCode,
            Permanent = relayException?.IsPermanent ?? false,
            At = UnixTimeUtc.Now(),
        };

        using var scope = serviceProvider.CreateScope();
        var table = scope.ServiceProvider.GetRequiredService<TableSettings>();

        await table.UpsertAsync(new SettingsRecord
        {
            key = Key(domain),
            value = OdinSystemSerializer.Serialize(failure),
        });
    }

    public async Task ClearAsync(string domain)
    {
        using var scope = serviceProvider.CreateScope();
        var table = scope.ServiceProvider.GetRequiredService<TableSettings>();

        await table.DeleteAsync(Key(domain));
    }
}
