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
/// (docs/email-keys-plan.md): the chosen primary address, and whether the mailbox is encrypted
/// or standard. Everything else about setup progress is already observable: the drive is mounted
/// or not, a public key is published or not, credential files exist on the drive or not.
/// Recording the address here is what lets the client resume an interrupted setup without
/// keeping a progress file of its own.
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
    /// Whether mail is usable: app passwords can be issued and autoconfig is served. A standard
    /// mailbox has no key to wait for. No record means the owner-console path, which is encrypted.
    /// </summary>
    public static bool IsMailReady(EmailSetupRecord? setup, PublishedEmailPublicKey? publishedKey)
    {
        return setup?.Mode == MailboxMode.Standard ? setup.MailboxProvisioned : publishedKey != null;
    }

    /// <summary>
    /// Idempotent: a re-run keeps the original provisioning timestamp, so a retry does not look
    /// like a fresh provision, and the original mode, which only changes through a mode switch.
    /// </summary>
    public Task MarkMailboxProvisionedAsync(string primaryEmailAddress, MailboxMode mode) =>
        UpdateAsync(r => r with
        {
            PrimaryEmailAddress = primaryEmailAddress,
            MailboxProvisioned = true,
            MailboxProvisionedAt = r.MailboxProvisioned ? r.MailboxProvisionedAt : UnixTimeUtc.Now(),
            Mode = r.MailboxProvisioned ? r.Mode : mode,
        });

    /// <summary>
    /// Points at the drive file holding the current secret keyring. Rotation moves this pointer;
    /// the file it used to name is never deleted, so older mail stays decryptable.
    /// </summary>
    public Task SetCurrentKeyAsync(Guid keyFileUniqueId) =>
        UpdateAsync(r => r with { CurrentKeyFileUniqueId = keyFileUniqueId });

    public Task SetModeAsync(MailboxMode mode) => UpdateAsync(r => r with { Mode = mode });

    /// <summary>Tenant deletion / teardown ride-along.</summary>
    public async Task DeleteAsync()
    {
        await Storage.DeleteAsync(identityDatabase.KeyValueCached, SetupRecordKey);
    }

    private async Task UpdateAsync(Func<EmailSetupRecord, EmailSetupRecord> change)
    {
        var record = change(await GetAsync() ?? new EmailSetupRecord());
        await Storage.UpsertAsync(identityDatabase.KeyValueCached, SetupRecordKey, record);
    }
}

public record EmailSetupRecord
{
    public string PrimaryEmailAddress { get; init; } = "";
    public bool MailboxProvisioned { get; init; }
    public UnixTimeUtc MailboxProvisionedAt { get; init; }
    public Guid? CurrentKeyFileUniqueId { get; init; }

    // Absent in records written before modes existed, which were all encrypted
    public MailboxMode Mode { get; init; } = MailboxMode.Encrypted;
}

/// <summary>
/// Whether the mail server encrypts stored mail to the identity's OpenPGP key (only OpenPGP
/// clients can read it) or stores it as received (any mail app can).
/// </summary>
public enum MailboxMode
{
    Encrypted = 0,
    Standard = 1,
}
