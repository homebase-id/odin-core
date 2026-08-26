using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Email.Dkim;
using Odin.Services.Email.Mailbox;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Email;

#nullable enable

/// <summary>
/// The server side of email activation (docs/email-keys-plan.md "Activation flow").
/// Idempotent: the app calls it after creating the email drive and keypair; re-runs
/// (and rotation, by sending a new certificate) are safe. Steps: DKIM keys
/// (generate once, store server-operational), DKIM DNS records (written, or handed
/// back as instructions for manual-DNS tenants), publish the E2E public certificate
/// (WKD/DID/autoconfig go live), provision the mailbox through IMailboxProvider.
/// </summary>
public class MailActivationService(
    ILogger<MailActivationService> logger,
    OdinConfiguration configuration,
    TenantContext tenantContext,
    IDkimStore dkimStore,
    IIdentityRegistrationService identityRegistrationService,
    IDnsLookupService dnsLookupService,
    EmailPublicKeyService emailPublicKeyService,
    IMailboxProvider mailboxProvider)
{
    /// <summary>
    /// Activation in one call, for the owner console: the mailbox, then the key. Behaviour and
    /// wire shape are unchanged — it is now the two halves below in sequence, so the app flow
    /// can run them separately and generate its key last.
    /// </summary>
    public async Task<MailActivationResult> ActivateAsync(string publicCertificateArmored, string primaryEmailAddress)
    {
        ThrowIfTenantMailDisabled();

        // Validate the certificate BEFORE any side effect (DKIM generation, DNS writes)
        AssertPublishableCertificate(publicCertificateArmored);

        var result = await EnsureMailboxAsync(primaryEmailAddress);
        await PublishKeyAsync(publicCertificateArmored);
        return result;
    }

    /// <summary>
    /// Everything that does not need the encryption key: DKIM keys, their DNS records, the
    /// mailbox itself and its DKIM signing keys. Idempotent — a re-run keeps the existing DKIM
    /// pair, because rotation is a deliberate separate action rather than a setup side effect.
    ///
    /// Split out so setup can create the mailbox first and generate the key last: mail may
    /// already arrive before a key exists, and it starts being encrypted the moment one does.
    /// </summary>
    public async Task<MailActivationResult> EnsureMailboxAsync(string primaryEmailAddress)
    {
        ThrowIfTenantMailDisabled();

        var domain = tenantContext.HostOdinId.DomainName;

        if (string.IsNullOrWhiteSpace(primaryEmailAddress) ||
            !primaryEmailAddress.EndsWith($"@{domain}", StringComparison.OrdinalIgnoreCase))
        {
            throw new OdinClientException($"Primary email address must be an address at {domain}");
        }

        // 1. DKIM keys: generate once; a re-run keeps the existing pair (rotation is
        //    a deliberate separate action, not an activation side effect)
        var dkimKeys = await dkimStore.GetKeysAsync(domain);
        if (dkimKeys.Count == 0)
        {
            dkimKeys = DkimKeyGenerator.GenerateKeys();
            await dkimStore.SaveKeysAsync(domain, dkimKeys);
            logger.LogInformation("Generated DKIM keys for {domain}", domain);
        }

        // 2. DKIM DNS records - written when the tenant's DNS is ours, otherwise the
        //    caller shows them as instructions (manual-records tenants)
        var dkimRecords = DkimDnsRecords.ToDnsConfigs(domain, dkimKeys);
        var recordsWritten = await identityRegistrationService.WriteOnActivationRecords(
            new AsciiDomainName(domain), dkimRecords);

        // 3. Provision the mail server (Null provider until one exists)
        await mailboxProvider.CreateMailboxAsync(domain, primaryEmailAddress);
        foreach (var key in dkimKeys)
        {
            await mailboxProvider.SetDkimKeyAsync(domain, key);
        }

        logger.LogInformation("Mailbox ready for {domain} (DNS records written: {written})", domain, recordsWritten);

        return new MailActivationResult
        {
            DnsRecordsWritten = recordsWritten,
            DkimRecords = dkimRecords,
        };
    }

    /// <summary>
    /// (Re)publishes this tenant's static mail DNS records - MX, SPF, DMARC, MTA-STS,
    /// TLS-RPT and the mta-sts CNAME.
    ///
    /// Those records are config-derived and identical for every tenant, so they are normally
    /// written at PROVISIONING time (<see cref="IIdentityRegistrationService.CreateManagedDomain"/>
    /// -> EnsureManagedDomainRecords). A tenant provisioned before Email:TenantMail was enabled
    /// never got them: activating email afterwards writes the per-tenant DKIM records and
    /// nothing else, which leaves a mailbox that works outbound but has no MX to receive on.
    /// The CLI backfill fixes that fleet-wide; this is the owner's own button for one identity.
    ///
    /// Idempotent - rrset writes are REPLACE - so the whole set is published rather than
    /// diffing which records are currently broken.
    ///
    /// Returns <see cref="MailDnsPublishResult.DnsRecordsWritten"/> false when the tenant's DNS
    /// is not ours to write (manual-records/BYOD tenants, or a host without PowerDNS access).
    /// The records are still returned so the caller can show them as instructions.
    /// </summary>
    public async Task<MailDnsPublishResult> PublishMailDnsRecordsAsync()
    {
        ThrowIfTenantMailDisabled();

        var domain = new AsciiDomainName(tenantContext.HostOdinId.DomainName);

        // Optional == the mail record set. The other optional-ish record, www, is not flagged
        // this way - it is probed separately (DnsHealthService.CheckOptionalWwwAsync) - and
        // DnsHealthService relies on this same equivalence to build its MailRecords list.
        var records = dnsLookupService.GetDnsConfiguration(domain).Where(x => x.Optional).ToList();

        if (records.Count == 0)
        {
            // Tenant mail is on but the config names no mail infrastructure. Nothing to write,
            // and writing an empty set would be indistinguishable from success to the caller.
            logger.LogWarning("No mail DNS records to publish for {domain}; check Email:TenantMail config", domain);
            return new MailDnsPublishResult { DnsRecordsWritten = false, Records = records };
        }

        var written = await identityRegistrationService.WriteOnActivationRecords(domain, records);

        logger.LogInformation(
            "Mail DNS records published for {domain}: {count} record(s), written={written}",
            domain, records.Count, written);

        return new MailDnsPublishResult { DnsRecordsWritten = written, Records = records };
    }

    /// <summary>
    /// Publishes the E2E public certificate — WKD, the DID keyAgreement entry and autoconfig go
    /// live with this — and hands it to the mail server for encryption-at-rest.
    ///
    /// Call order matters and is the caller's responsibility: the secret keyring must already be
    /// durable before this runs. Once a certificate is published, mail arriving is encrypted to
    /// it, and a key nobody holds means mail nobody can read.
    /// </summary>
    public async Task PublishKeyAsync(string publicCertificateArmored)
    {
        ThrowIfTenantMailDisabled();
        AssertPublishableCertificate(publicCertificateArmored);

        var domain = tenantContext.HostOdinId.DomainName;

        await emailPublicKeyService.PublishAsync(publicCertificateArmored);
        await mailboxProvider.SetEncryptionKeyAsync(domain, publicCertificateArmored);

        logger.LogInformation("Published the email encryption key for {domain}", domain);
    }

    private static void AssertPublishableCertificate(string publicCertificateArmored)
    {
        try
        {
            Odin.Core.Cryptography.Pgp.OpenPgpKeyManagement.GetEncryptionSubkeySpkiDer(publicCertificateArmored);
        }
        catch (Exception e)
        {
            throw new OdinClientException("Not a valid OpenPGP public certificate with an encryption subkey", inner: e);
        }
    }

    public async Task<MailStatusResult> GetStatusAsync()
    {
        var domain = tenantContext.HostOdinId.DomainName;

        var publishedKey = await emailPublicKeyService.GetPublishedKeyAsync();
        var dkimKeys = dkimStore.IsConfigured ? await dkimStore.GetKeysAsync(domain) : [];

        return new MailStatusResult
        {
            TenantMailEnabled = configuration.Email.TenantMail.Enabled,
            Activated = publishedKey != null,
            PublicKeyFingerprint = publishedKey?.FingerprintHex,
            PublishedAt = publishedKey?.PublishedAt,
            DkimRecords = DkimDnsRecords.ToDnsConfigs(domain, dkimKeys),
        };
    }

    public async Task<string> ProvisionAppPasswordAsync(string primaryEmailAddress, string label)
    {
        ThrowIfTenantMailDisabled();

        var domain = tenantContext.HostOdinId.DomainName;

        if (await emailPublicKeyService.GetPublishedKeyAsync() == null)
        {
            throw new OdinClientException("Email is not activated");
        }

        // The provider generates the secret and returns it exactly once (live-verified:
        // Stalwart's AppPassword.secret is serverSet), and Homebase stores it nowhere. The
        // owner path keeps its string-only wire shape; the app path uses IssueAppPasswordAsync
        // below, which also keeps the id it needs to revoke with.
        var provision = await mailboxProvider.ProvisionAppPasswordAsync(domain, primaryEmailAddress, label);
        return provision.Secret;
    }

    /// <summary>
    /// Issue an app password and keep the provider's id for it. The app stores both on the email
    /// drive: the secret because the mail server will never show it again, and the id because it
    /// is the only handle a later revoke has.
    /// </summary>
    public async Task<AppPasswordProvision> IssueAppPasswordAsync(string primaryEmailAddress, string label)
    {
        ThrowIfTenantMailDisabled();

        var domain = tenantContext.HostOdinId.DomainName;

        if (await emailPublicKeyService.GetPublishedKeyAsync() == null)
        {
            throw new OdinClientException("Email is not activated");
        }

        return await mailboxProvider.ProvisionAppPasswordAsync(domain, primaryEmailAddress, label);
    }

    /// <summary>
    /// Revoke an issued app password. Not gated on activation: revoking must keep working even
    /// if the key was unpublished, or a credential could be stranded live on the mail server.
    /// </summary>
    public async Task RevokeAppPasswordAsync(string appPasswordId)
    {
        ThrowIfTenantMailDisabled();
        await mailboxProvider.RevokeAppPasswordAsync(tenantContext.HostOdinId.DomainName, appPasswordId);
    }

    /// <summary>Mailbox state, or null when the provider cannot answer.</summary>
    public async Task<MailboxStatus?> GetMailboxStatusAsync()
    {
        ThrowIfTenantMailDisabled();
        return await mailboxProvider.GetMailboxStatusAsync(tenantContext.HostOdinId.DomainName);
    }

    /// <summary>
    /// The server half of the owner-console encrypt/decrypt round-trip check
    /// (docs/email-keys-plan.md): a random nonce encrypted to the PUBLISHED public
    /// certificate, plus the nonce's SHA-256. The client decrypts with the private
    /// keyring from the email drive and compares hashes - failure is critical-grade
    /// (incoming mail is being encrypted to a key the owner cannot decrypt), and no
    /// unattended check can catch it. Stateless: the server keeps nothing.
    /// </summary>
    public async Task<MailRoundTripChallenge> CreateRoundTripChallengeAsync()
    {
        var publishedKey = await emailPublicKeyService.GetPublishedKeyAsync();
        if (publishedKey == null)
        {
            throw new OdinClientException("Email is not activated");
        }

        var nonce = ByteArrayUtil.GetRndByteArray(32);
        var encrypted = Odin.Core.Cryptography.Pgp.OpenPgpKeyManagement.Encrypt(nonce, publishedKey.PublicCertificateArmored);

        return new MailRoundTripChallenge
        {
            EncryptedNonceBase64 = Convert.ToBase64String(encrypted),
            NonceSha256Base64 = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(nonce)),
        };
    }

    //

    private void ThrowIfTenantMailDisabled()
    {
        if (!configuration.Email.TenantMail.Enabled)
        {
            throw new OdinClientException("Tenant mail is not enabled on this host");
        }
    }

}

public class MailActivationResult
{
    public bool DnsRecordsWritten { get; init; }
    public List<DnsConfig> DkimRecords { get; init; } = [];
}

public class MailRoundTripChallenge
{
    /// <summary>OpenPGP message holding the nonce, encrypted to the published certificate.</summary>
    public string EncryptedNonceBase64 { get; init; } = "";

    /// <summary>SHA-256 of the nonce - what a successful client-side decryption must hash to.</summary>
    public string NonceSha256Base64 { get; init; } = "";
}

/// <summary>
/// Outcome of <see cref="MailActivationService.PublishMailDnsRecordsAsync"/>.
/// <see cref="Records"/> is populated either way: when <see cref="DnsRecordsWritten"/> is
/// false the caller shows them as manual instructions instead.
/// </summary>
public class MailDnsPublishResult
{
    public bool DnsRecordsWritten { get; init; }
    public List<DnsConfig> Records { get; init; } = [];
}

public class MailStatusResult
{
    public bool TenantMailEnabled { get; init; }
    public bool Activated { get; init; }
    public string? PublicKeyFingerprint { get; init; }
    public UnixTimeUtc? PublishedAt { get; init; }
    public List<DnsConfig> DkimRecords { get; init; } = [];
}
