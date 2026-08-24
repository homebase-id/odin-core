using System;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Email;

#nullable enable

/// <summary>
/// The small amount of email setup state the client cannot derive from anywhere else
/// (docs/email-keys-plan.md). Everything else about setup progress is already observable:
/// the drive is mounted or not, a public key is published or not, credential files exist on
/// the drive or not. The chosen primary address is the exception — nothing else records it —
/// and recording it here is what lets the client resume an interrupted setup without keeping
/// a progress file of its own.
///
/// Written by the app-facing setup flow; the owner activation path does not use it.
/// </summary>
public class EmailSetupStateService(IdentityDatabase identityDatabase)
{
    private const string ContextKey = "3d6c9a17-5f42-4e08-b1d3-7a4e02c58b96";

    private static readonly SingleKeyValueStorage Storage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ContextKey));

    private static readonly Guid SetupRecordKey = Guid.Parse("b0e5f4c2-8d71-4a36-9c2f-1e63d095a7f4");

    public async Task<EmailSetupRecord?> GetAsync()
    {
        return await Storage.GetAsync<EmailSetupRecord>(identityDatabase.KeyValueCached, SetupRecordKey);
    }

    /// <summary>
    /// Records that the mailbox exists for <paramref name="primaryEmailAddress"/>. Idempotent:
    /// re-running the mailbox step keeps the original provisioning timestamp so a retry does not
    /// look like a fresh provision.
    /// </summary>
    public async Task MarkMailboxProvisionedAsync(string primaryEmailAddress)
    {
        var existing = await GetAsync();

        var record = new EmailSetupRecord
        {
            PrimaryEmailAddress = primaryEmailAddress,
            MailboxProvisioned = true,
            MailboxProvisionedAt = existing?.MailboxProvisioned == true
                ? existing.MailboxProvisionedAt
                : UnixTimeUtc.Now(),
            CurrentKeyFileUniqueId = existing?.CurrentKeyFileUniqueId,
        };

        await Storage.UpsertAsync(identityDatabase.KeyValueCached, SetupRecordKey, record);
    }

    /// <summary>
    /// Points at the drive file holding the current secret keyring. Rotation moves this pointer;
    /// the file it used to name is never deleted, so older mail stays decryptable.
    /// </summary>
    public async Task SetCurrentKeyAsync(Guid keyFileUniqueId)
    {
        var existing = await GetAsync();

        var record = new EmailSetupRecord
        {
            PrimaryEmailAddress = existing?.PrimaryEmailAddress ?? "",
            MailboxProvisioned = existing?.MailboxProvisioned ?? false,
            MailboxProvisionedAt = existing?.MailboxProvisionedAt ?? default,
            CurrentKeyFileUniqueId = keyFileUniqueId,
        };

        await Storage.UpsertAsync(identityDatabase.KeyValueCached, SetupRecordKey, record);
    }

    /// <summary>Tenant deletion / teardown ride-along.</summary>
    public async Task DeleteAsync()
    {
        await Storage.DeleteAsync(identityDatabase.KeyValueCached, SetupRecordKey);
    }
}

public class EmailSetupRecord
{
    public string PrimaryEmailAddress { get; init; } = "";
    public bool MailboxProvisioned { get; init; }
    public UnixTimeUtc MailboxProvisionedAt { get; init; }
    public Guid? CurrentKeyFileUniqueId { get; init; }
}
