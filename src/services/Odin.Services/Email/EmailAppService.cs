using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Pgp;
using Odin.Core.Util;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Dns.Health;
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
    EmailKeyMaterialWriter keyMaterialWriter,
    MailActivationService mailActivationService,
    DnsHealthService dnsHealthService,
    EmailHealthVerifier emailHealthVerifier)
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
            // Same values the autoconfig XML publishes, from the same definition. The app
            // shows them so someone can set up a mail client that has no autoconfig support,
            // or check what a client filled in for itself.
            ClientSettings = MailClientSettings.For(
                configuration.Email.TenantMail.MxNodes,
                setup?.PrimaryEmailAddress ?? ""),
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
    /// Generates the identity's OpenPGP keyring and puts it to work. The last setup step, and the
    /// only one whose ordering is load-bearing:
    ///
    ///   1. write the keyring to the email drive — durable, encrypted, owner-only
    ///   2. only then publish its certificate and hand it to the mail server
    ///
    /// Mail arriving after step 2 is encrypted to that certificate. Doing it the other way round
    /// would open a window where a published key has no readable private half, which is the
    /// unrecoverable row of the custody table in docs/email-keys-plan.md.
    ///
    /// Rotation is the same call again: a new keyring is appended, the pointer moves, and the old
    /// keyring stays exactly where it is so older mail keeps opening.
    /// </summary>
    public async Task<EmailKeyGenerationResult> GenerateKeyAsync(
        string primaryEmailAddress,
        byte[] clientEntropy,
        IOdinContext odinContext)
    {
        await AssertEmailDriveAccessAsync(odinContext);
        AssertTenantMailEnabled();

        var domain = tenantContext.HostOdinId.DomainName;
        if (string.IsNullOrWhiteSpace(primaryEmailAddress) ||
            !primaryEmailAddress.EndsWith($"@{domain}", StringComparison.OrdinalIgnoreCase))
        {
            throw new OdinClientException($"Primary email address must be an address at {domain}");
        }

        byte[]? seed = null;
        if (clientEntropy is { Length: > 0 })
        {
            // Whitened before it reaches the generator, and combined with fresh server randomness
            // so the seed is never wholly caller-controlled even before BouncyCastle mixes it in.
            // Raw accelerometer samples are correlated and low-entropy; treating them as a key
            // seed directly would be worse than not collecting them.
            seed = SHA512.HashData(ByteArrayUtil.Combine(clientEntropy, ByteArrayUtil.GetRndByteArray(64)));
        }

        var material = OpenPgpKeyManagement.GenerateP384KeyMaterial(primaryEmailAddress, seed);

        // Durable first.
        var keyFileUniqueId = await keyMaterialWriter.WriteKeyMaterialAsync(material, primaryEmailAddress, odinContext);
        await keyMaterialWriter.UpdateCurrentKeyPointerAsync(keyFileUniqueId, material.FingerprintHex, odinContext);

        // Then, and only then, publish.
        await mailActivationService.PublishKeyAsync(material.PublicCertificateArmored);
        await setupStateService.SetCurrentKeyAsync(keyFileUniqueId);

        return new EmailKeyGenerationResult
        {
            KeyFileUniqueId = keyFileUniqueId,
            FingerprintHex = material.FingerprintHex,
            ClientEntropyUsed = seed != null,
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
    public async Task<MailboxStatusResult> GetMailboxStatusAsync(IOdinContext odinContext)
    {
        await AssertEmailDriveAccessAsync(odinContext);
        AssertTenantMailEnabled();

        var status = await mailActivationService.GetMailboxStatusAsync();

        return status == null
            ? new MailboxStatusResult { Available = false }
            : new MailboxStatusResult
            {
                Available = true,
                UsedBytes = status.UsedBytes,
                QuotaBytes = status.QuotaBytes,
                InboxTotal = status.InboxTotal,
                InboxUnread = status.InboxUnread,
                JunkTotal = status.JunkTotal,
                QueuedOutbound = status.QueuedOutbound,
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

    /// <summary>
    /// The health of this identity's email, for the app's "check my email" button.
    ///
    /// Runs the SAME checks the owner console's Email tab runs, from the same services -
    /// <see cref="DnsHealthService"/> for the record rows, <see cref="EmailHealthVerifier"/> for
    /// the DKIM pair proof and public-key drift - so the two surfaces cannot disagree and the
    /// client reimplements none of it.
    ///
    /// On demand, deliberately NOT folded into <see cref="GetStatusAsync"/>: this does DNS
    /// lookups plus outbound HTTPS, and status is fetched on every login and identity switch.
    /// </summary>
    public async Task<MailAppHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.Email.TenantMail.Enabled)
        {
            return new MailAppHealthResult { TenantMailEnabled = false, Activated = false };
        }

        var domain = new AsciiDomainName(tenantContext.HostOdinId.DomainName);

        var dns = await dnsHealthService.GetDnsHealthAsync(domain, cancellationToken);
        var verification = await emailHealthVerifier.VerifyAsync(cancellationToken);

        return new MailAppHealthResult
        {
            TenantMailEnabled = true,
            Activated = verification.Activated,
            Records = dns.MailRecords,
            BrokenRecords = dns.MailRecords.Where(x => x.Status != DnsLookupRecordStatus.Success).ToList(),
            Errors = verification.Errors,
            Warnings = verification.Warnings,
        };
    }

}

public class EmailKeyGenerationResult
{
    /// <summary>The drive file holding the new keyring — the client reads it back from there.</summary>
    public Guid KeyFileUniqueId { get; init; }

    public string FingerprintHex { get; init; } = "";

    /// <summary>
    /// Whether the caller's entropy was mixed in. Reported honestly so a client that skipped the
    /// shake (desktop, web, or the user declining) is not told otherwise.
    /// </summary>
    public bool ClientEntropyUsed { get; init; }
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

public class MailboxStatusResult
{
    /// <summary>False when the mail server does not report; the UI then shows nothing.</summary>
    public bool Available { get; init; }

    public long UsedBytes { get; init; }

    /// <summary>Null means unlimited, or simply not reported.</summary>
    public long? QuotaBytes { get; init; }

    public int InboxTotal { get; init; }

    /// <summary>The number worth putting on a screen, and behind a badge.</summary>
    public int InboxUnread { get; init; }

    /// <summary>Mail that was filed as junk — where messages people expected go to look lost.</summary>
    public int JunkTotal { get; init; }

    /// <summary>Outbound messages still waiting. Above zero for long means delivery trouble.</summary>
    public int QueuedOutbound { get; init; }
}

/// <summary>
/// Whether this identity's email actually WORKS, as opposed to whether it has been set up.
/// <see cref="MailAppStatusResult"/> answers the second question only, so an identity whose
/// domain has no MX reports as fully configured while nothing can deliver mail to it.
/// </summary>
public class MailAppHealthResult
{
    public bool TenantMailEnabled { get; init; }

    /// <summary>False when email was never activated: nothing to report, rather than everything broken.</summary>
    public bool Activated { get; init; }

    /// <summary>The mail DNS rows, exactly as the owner console's Email tab shows them.</summary>
    public List<DnsConfig> Records { get; init; } = [];

    /// <summary>Rows that are missing or wrong, so the client filters nothing itself.</summary>
    public List<DnsConfig> BrokenRecords { get; init; } = [];

    /// <summary>Checks a record comparison cannot make: DKIM pair proof, public-key drift.</summary>
    public List<string> Errors { get; init; } = [];

    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// The one verdict a client should branch on, so "is my email healthy" is decided here
    /// rather than re-derived - differently - in each client.
    /// </summary>
    public bool NeedsAttention => BrokenRecords.Count > 0 || Errors.Count > 0;
}

/// <summary>
/// Status for the app surface. Deliberately NOT a reuse of the owner
/// <see cref="MailStatusResult"/>: the owner console and the app answer different questions, and
/// sharing the type would let one drag the other's wire shape around.
/// </summary>
public class MailAppStatusResult
{
    /// <summary>
    /// Hostnames, ports and username for a mail client. Null when this host publishes no mail
    /// hosts, so a client renders nothing rather than a half-filled form.
    /// </summary>
    public MailClientSettings? ClientSettings { get; init; }

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
