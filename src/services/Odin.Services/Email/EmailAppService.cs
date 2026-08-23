using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Core.Time;
using Odin.Services.Email.Dkim;
using Odin.Services.Email.Mailbox;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Email;

#nullable enable

/// <summary>
/// The app-facing half of email setup (docs/email-keys-plan.md), reached over
/// <c>/api/v2/mail</c> by chat-kmp's "Email setup" add-on. The owner endpoints under
/// <c>/api/owner/v1/mail</c> are unchanged and remain the owner console's surface.
///
/// Authorization is the caller's access to the Email app's drive: an app that holds Read and
/// Write on <see cref="WellKnownAppDrives.EmailAppDrive"/> is the identity's email app, and
/// nothing else is. Mail is an external system rather than a Homebase feature, so there is no
/// mail permission key to grant; the drive the app already had to be granted is the signal.
/// </summary>
public class EmailAppService(
    OdinConfiguration configuration,
    TenantContext tenantContext,
    IDriveManager driveManager,
    IDkimStore dkimStore,
    EmailPublicKeyService emailPublicKeyService,
    EmailSetupStateService setupStateService,
    MailActivationService mailActivationService)
{
    /// <summary>
    /// The status the client renders its whole entry flow from. Deliberately ungated: the app
    /// must be able to say "this server has no email" before the drive exists, and to tell that
    /// apart from "you have not set it up yet". Nothing here is secret.
    /// </summary>
    public async Task<MailAppStatusResult> GetStatusAsync(IOdinContext odinContext)
    {
        var domain = tenantContext.HostOdinId.DomainName;

        var publishedKey = await emailPublicKeyService.GetPublishedKeyAsync();
        var dkimKeys = dkimStore.IsConfigured ? await dkimStore.GetKeysAsync(domain) : [];
        var setup = await setupStateService.GetAsync();

        return new MailAppStatusResult
        {
            TenantMailEnabled = configuration.Email.TenantMail.Enabled,
            DriveProvisioned = await HasEmailDriveAccessAsync(odinContext),
            MailboxProvisioned = setup?.MailboxProvisioned ?? false,
            PrimaryEmailAddress = setup?.PrimaryEmailAddress,
            Activated = publishedKey != null,
            PublicKeyFingerprint = publishedKey?.FingerprintHex,
            PublishedAt = publishedKey?.PublishedAt,
            DkimRecords = DkimDnsRecords.ToDnsConfigs(domain, dkimKeys),
            CurrentKeyFileUniqueId = setup?.CurrentKeyFileUniqueId,
        };
    }

    /// <summary>
    /// Server half of the encrypt/decrypt round-trip check: the client decrypts the returned
    /// message with the keyring from its email drive and compares the hash. Gated on drive
    /// access but NOT on the tenant-mail flag — a tenant whose key is published can always
    /// verify it can still read its own mail.
    /// </summary>
    public async Task<MailRoundTripChallenge> CreateRoundTripChallengeAsync(IOdinContext odinContext)
    {
        await AssertEmailDriveAccessAsync(odinContext);
        return await mailActivationService.CreateRoundTripChallengeAsync();
    }

    /// <summary>
    /// Creates the mailbox: DKIM keys, their DNS records, the account, and the DKIM signing keys
    /// on the mail server. Idempotent, so a client that was killed mid-setup simply calls it
    /// again. Records the chosen address so setup can resume without the client tracking it.
    ///
    /// No key yet — that is the last step. Mail can already arrive; it starts being encrypted
    /// the moment a key exists.
    /// </summary>
    public async Task<MailboxSetupResult> EnsureMailboxAsync(string primaryEmailAddress, IOdinContext odinContext)
    {
        await AssertEmailDriveAccessAsync(odinContext);
        AssertTenantMailEnabled();

        var result = await mailActivationService.EnsureMailboxAsync(primaryEmailAddress);
        await setupStateService.MarkMailboxProvisionedAsync(primaryEmailAddress);

        return new MailboxSetupResult
        {
            PrimaryEmailAddress = primaryEmailAddress,
            DnsRecordsWritten = result.DnsRecordsWritten,
            DkimRecords = result.DkimRecords,
        };
    }

    /// <summary>
    /// Issues a mail-client credential. The secret crosses this API exactly once — the mail
    /// server generates it and will not show it again — so the client must write it to the email
    /// drive before showing it to anyone. The id comes back too: it is the only handle a later
    /// revoke has.
    /// </summary>
    public async Task<AppPasswordIssueResult> IssueAppPasswordAsync(
        string primaryEmailAddress,
        string label,
        IOdinContext odinContext)
    {
        await AssertEmailDriveAccessAsync(odinContext);
        AssertTenantMailEnabled();

        var provision = await mailActivationService.IssueAppPasswordAsync(primaryEmailAddress, label);

        return new AppPasswordIssueResult
        {
            Id = provision.Id,
            Secret = provision.Secret,
            Label = label,
            CreatedAt = UnixTimeUtc.Now(),
        };
    }

    /// <summary>
    /// Revokes a credential on the mail server. Deleting the client's own record of it revokes
    /// nothing — this is the call that actually stops it working.
    /// </summary>
    public async Task RevokeAppPasswordAsync(string appPasswordId, IOdinContext odinContext)
    {
        await AssertEmailDriveAccessAsync(odinContext);
        AssertTenantMailEnabled();

        await mailActivationService.RevokeAppPasswordAsync(appPasswordId);
    }

    /// <summary>
    /// Mailbox storage for the status screen. A provider that cannot answer yields
    /// Available = false rather than an error: this is one line on a screen, not a reason to
    /// fail the screen.
    /// </summary>
    public async Task<MailStorageResult> GetStorageAsync(IOdinContext odinContext)
    {
        await AssertEmailDriveAccessAsync(odinContext);
        AssertTenantMailEnabled();

        var usage = await mailActivationService.GetUsageAsync();

        return usage == null
            ? new MailStorageResult { Available = false }
            : new MailStorageResult
            {
                Available = true,
                UsedBytes = usage.UsedBytes,
                QuotaBytes = usage.QuotaBytes,
            };
    }

    /// <summary>
    /// The gate every mail ACTION opens with. Order matters and is load-bearing:
    ///
    /// 1. the drive must exist          -> 400, "you have not approved the app's drive yet"
    /// 2. the caller must hold R and W  -> 403, "you are not this identity's email app"
    ///
    /// and the caller then applies <see cref="AssertTenantMailEnabled"/> LAST, so a fully
    /// authorized caller on a host without email gets 400 rather than 403. That ordering is
    /// also what makes the whole access-control matrix testable while the feature flag is off
    /// everywhere, which it is and must remain until MX nodes exist.
    ///
    /// Read AND Write, not either: every mail action both writes key material to the drive and
    /// reads it back.
    /// </summary>
    public async Task AssertEmailDriveAccessAsync(IOdinContext odinContext)
    {
        var driveId = WellKnownAppDrives.EmailAppDrive.Alias;

        // OdinClientException(InvalidDrive) -> 400
        await driveManager.GetDriveAsync(driveId, failIfInvalid: true);

        // OdinSecurityException -> 403
        odinContext.PermissionsContext.AssertCanReadDrive(driveId);
        odinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
    }

    public void AssertTenantMailEnabled()
    {
        if (!configuration.Email.TenantMail.Enabled)
        {
            throw new OdinClientException("Tenant mail is not enabled on this host");
        }
    }

    /// <summary>
    /// Non-throwing form of the gate, for reporting rather than enforcing. A missing drive and a
    /// missing grant are the same answer here: the app cannot use the drive.
    /// </summary>
    private async Task<bool> HasEmailDriveAccessAsync(IOdinContext odinContext)
    {
        var driveId = WellKnownAppDrives.EmailAppDrive.Alias;

        var drive = await driveManager.GetDriveAsync(driveId);
        if (drive == null)
        {
            return false;
        }

        return odinContext.PermissionsContext.HasDrivePermission(driveId, DrivePermission.Read) &&
               odinContext.PermissionsContext.HasDrivePermission(driveId, DrivePermission.Write);
    }
}

public class MailboxSetupResult
{
    public string PrimaryEmailAddress { get; init; } = "";

    /// <summary>False for manual-DNS tenants — the records are shown as instructions instead.</summary>
    public bool DnsRecordsWritten { get; init; }

    public List<DnsConfig> DkimRecords { get; init; } = [];
}

/// <summary>
/// A newly issued mail-client credential. <see cref="Secret"/> is in transit exactly once.
/// </summary>
public class AppPasswordIssueResult
{
    /// <summary>The mail server's id for this credential — needed to revoke it later.</summary>
    public string Id { get; init; } = "";

    public string Secret { get; init; } = "";
    public string Label { get; init; } = "";
    public Core.Time.UnixTimeUtc CreatedAt { get; init; }
}

public class MailStorageResult
{
    /// <summary>False when the mail server does not report usage; the UI then shows nothing.</summary>
    public bool Available { get; init; }

    public long UsedBytes { get; init; }

    /// <summary>Null means unlimited, or simply not reported.</summary>
    public long? QuotaBytes { get; init; }
}

/// <summary>
/// Status for the app surface. Deliberately NOT a reuse of the owner
/// <see cref="MailStatusResult"/>: the owner console and the app answer different questions, and
/// sharing the type would let one drag the other's wire shape around.
/// </summary>
public class MailAppStatusResult
{
    /// <summary>Whether this host runs tenant mail at all. False everywhere today.</summary>
    public bool TenantMailEnabled { get; init; }

    /// <summary>The email drive exists and the caller holds Read+Write on it.</summary>
    public bool DriveProvisioned { get; init; }

    public bool MailboxProvisioned { get; init; }

    public string? PrimaryEmailAddress { get; init; }

    /// <summary>A public certificate is published — the server-side "email is on" signal.</summary>
    public bool Activated { get; init; }

    public string? PublicKeyFingerprint { get; init; }

    public Core.Time.UnixTimeUtc? PublishedAt { get; init; }

    public List<DnsConfig> DkimRecords { get; init; } = [];

    /// <summary>The drive file holding the current secret keyring, once one exists.</summary>
    public System.Guid? CurrentKeyFileUniqueId { get; init; }
}
