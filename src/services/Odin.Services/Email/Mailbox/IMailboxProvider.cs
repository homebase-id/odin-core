#nullable enable
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

    /// <summary>
    /// Removes DKIM signatures the mail server generated for itself, keeping only
    /// <paramref name="ourSelectors"/>. Those self-generated keys have no DNS record, so every
    /// message they sign carries a signature no verifier can resolve.
    /// </summary>
    Task RemoveForeignDkimSignaturesAsync(string domain, IReadOnlyCollection<string> ourSelectors);

    /// <summary>The local parts the one mailbox answers to (chat-kmp EMAIL_APP.md).</summary>
    Task SetAliasesAsync(string domain, IReadOnlyCollection<string> localParts);

    /// <summary>Tenant deletion ride-along: remove the account and its mail data.</summary>
    Task DeleteMailboxAsync(string domain);

    /// <summary>
    /// Issue an app password for client auth (IMAP/SMTP/JMAP). The PROVIDER generates the
    /// secret and returns it exactly once — Stalwart's app passwords are server-generated and
    /// never readable again, so an interface that accepted a password could not be implemented
    /// against it honestly.
    /// </summary>
    Task<AppPasswordProvision> ProvisionAppPasswordAsync(string domain, string primaryAddress, string label);

    /// <summary>
    /// Revoke a previously issued app password. Idempotent by contract: an unknown or
    /// already-revoked id is a no-op, not an error — the caller is usually reconciling its own
    /// record of what it issued, and a failure there would strand a live credential.
    /// </summary>
    Task RevokeAppPasswordAsync(string domain, string appPasswordId);

    /// <summary>
    /// How the mailbox is doing: what it holds, what is unread, and whether anything is stuck
    /// on the way out. Returns null when the provider cannot answer — this feeds a status
    /// screen, so it must degrade to "not shown" rather than fail the screen.
    /// </summary>
    Task<MailboxStatus?> GetMailboxStatusAsync(string domain);
}

/// <summary>
/// A newly issued app password: the secret, which exists in transit exactly once, plus the
/// provider's id for it — the only handle a later revoke has.
/// </summary>
public sealed record AppPasswordProvision(string Id, string Secret);

/// <summary>
/// A mailbox's observable state.
///
/// The unread count is the one a person cares about; the queue depth is the one that means
/// something is wrong. Storage is here because it was already available, but it reads zero for
/// months on a normal mailbox.
/// </summary>
/// <param name="UsedBytes">Disk in use.</param>
/// <param name="QuotaBytes">Null means unlimited, or simply not reported.</param>
/// <param name="InboxTotal">Messages in the inbox.</param>
/// <param name="InboxUnread">Unread messages in the inbox.</param>
/// <param name="JunkTotal">Messages filed as junk — worth surfacing, because mail people expected
/// can end up here and look lost.</param>
/// <param name="QueuedOutbound">Messages still waiting to go out. Anything above zero for long is
/// a delivery problem, not a normal state.</param>
public sealed record MailboxStatus(
    long UsedBytes,
    long? QuotaBytes,
    int InboxTotal,
    int InboxUnread,
    int JunkTotal,
    int QueuedOutbound);
