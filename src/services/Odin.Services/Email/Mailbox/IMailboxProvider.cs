using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Email.Dkim;

namespace Odin.Services.Email.Mailbox;

/// <summary>
/// The mail-server wrapper seam (docs/email-keys-plan.md "The Stalwart wrapper"),
/// following the IDnsRestClient/PowerDNS precedent: one implementation per mail
/// server product, no product types leaking upward. Homebase is the key AUTHORITY
/// (generates, stores source of truth, publishes DNS) and provisions copies into
/// the mail server through this interface; the mail server is the operational
/// signer and store.
///
/// Hard custody line: implementations receive the E2E PUBLIC certificate only -
/// the private keyring must never cross this interface.
///
/// All methods are idempotent by contract: activation re-runs them freely.
/// </summary>
public interface IMailboxProvider
{
    /// <summary>Create the account + domain association for the tenant's one mailbox.</summary>
    Task CreateMailboxAsync(string domain, string primaryAddress);

    /// <summary>Upload the E2E PUBLIC certificate and enable encryption-at-rest with it.</summary>
    Task SetEncryptionKeyAsync(string domain, string publicCertificateArmored);

    /// <summary>Install the domain's DKIM signing key (per selector).</summary>
    Task SetDkimKeyAsync(string domain, DkimKey key);

    /// <summary>The local parts the one mailbox answers to (chat-kmp EMAIL_APP.md).</summary>
    Task SetAliasesAsync(string domain, IReadOnlyCollection<string> localParts);

    /// <summary>Tenant deletion ride-along: remove the account and its mail data.</summary>
    Task DeleteMailboxAsync(string domain);

    /// <summary>
    /// Provision an app password for client auth (IMAP/SMTP/JMAP) and RETURN the
    /// clear-text secret - shown to the owner exactly once, stored nowhere in
    /// Homebase. The mail server is the generator (live-verified against Stalwart:
    /// AppPassword.secret is serverSet); implementations without a server generate
    /// their own.
    /// </summary>
    Task<string> ProvisionAppPasswordAsync(string domain, string primaryAddress, string label);
}
