using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.Database.System.Table;

#nullable enable

namespace Odin.Services.Email.Relay;

/// <summary>
/// Why the relay last refused a domain, in its own words, when it did.
///
/// Onboarding runs in a job, and a job's error lives on its row - which has no identity, is
/// read by nothing, and is purged a day after the job gives up. On 2026-10-07 that was the only
/// trace of a tenant whose mail could not leave, while every health surface said all was well.
/// This is the trace the health check and the Email tab can actually read.
/// </summary>
public interface IMailRelayFailureStore
{
    Task<string?> GetAsync(string domain);
    Task RecordAsync(string domain, string message);
    Task ClearAsync(string domain);
}

/// <summary>
/// System settings table, not per-tenant storage: the onboarding job runs in system scope
/// with no tenant database, and the system database is shared by every node, so the failure
/// recorded by one node's job is what another node's health check reads.
/// </summary>
public class MailRelayFailureStore(IServiceProvider serviceProvider) : IMailRelayFailureStore
{
    private static string Key(string domain) => $"mail-relay-failure:{domain.ToLowerInvariant()}";

    public async Task<string?> GetAsync(string domain)
    {
        var record = await WithTable(table => table.GetAsync(Key(domain)));
        return string.IsNullOrEmpty(record?.value) ? null : record.value;
    }

    public Task RecordAsync(string domain, string message) =>
        WithTable(table => table.UpsertAsync(new SettingsRecord { key = Key(domain), value = message }));

    public Task ClearAsync(string domain) => WithTable(table => table.DeleteAsync(Key(domain)));

    private async Task<T> WithTable<T>(Func<TableSettings, Task<T>> action)
    {
        using var scope = serviceProvider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TableSettings>());
    }
}
