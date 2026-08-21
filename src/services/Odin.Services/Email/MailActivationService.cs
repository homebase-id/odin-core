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
    EmailPublicKeyService emailPublicKeyService,
    IMailboxProvider mailboxProvider)
{
    public async Task<MailActivationResult> ActivateAsync(string publicCertificateArmored, string primaryEmailAddress)
    {
        ThrowIfTenantMailDisabled();

        var domain = tenantContext.HostOdinId.DomainName;

        if (string.IsNullOrWhiteSpace(primaryEmailAddress) ||
            !primaryEmailAddress.EndsWith($"@{domain}", StringComparison.OrdinalIgnoreCase))
        {
            throw new OdinClientException($"Primary email address must be an address at {domain}");
        }

        // Validate the certificate BEFORE any side effect (DKIM generation, DNS writes)
        try
        {
            Odin.Core.Cryptography.Pgp.OpenPgpKeyManagement.GetEncryptionSubkeySpkiDer(publicCertificateArmored);
        }
        catch (Exception e)
        {
            throw new OdinClientException("Not a valid OpenPGP public certificate with an encryption subkey", inner: e);
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

        // 3. Publish the E2E public certificate (validates before storing); WKD, the
        //    DID keyAgreement entry, and autoconfig go live with this
        await emailPublicKeyService.PublishAsync(publicCertificateArmored);

        // 4. Provision the mail server (Null provider until one exists)
        await mailboxProvider.CreateMailboxAsync(domain, primaryEmailAddress);
        await mailboxProvider.SetEncryptionKeyAsync(domain, publicCertificateArmored);
        foreach (var key in dkimKeys)
        {
            await mailboxProvider.SetDkimKeyAsync(domain, key);
        }

        logger.LogInformation("Email activated for {domain} (DNS records written: {written})", domain, recordsWritten);

        return new MailActivationResult
        {
            DnsRecordsWritten = recordsWritten,
            DkimRecords = dkimRecords,
        };
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

        // The provider generates and installs the secret (live-verified: Stalwart's
        // AppPassword.secret is serverSet); returned to the owner exactly once,
        // stored nowhere in Homebase
        return await mailboxProvider.ProvisionAppPasswordAsync(domain, primaryEmailAddress, label);
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

public class MailStatusResult
{
    public bool TenantMailEnabled { get; init; }
    public bool Activated { get; init; }
    public string? PublicKeyFingerprint { get; init; }
    public UnixTimeUtc? PublishedAt { get; init; }
    public List<DnsConfig> DkimRecords { get; init; } = [];
}
