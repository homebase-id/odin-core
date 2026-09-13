#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Microsoft.Extensions.Logging;
using Odin.Services.Email.Dkim;

namespace Odin.Services.Email.Mailbox;

/// <summary>
/// The no-mail-server implementation (NullEmailSender pattern): logs and succeeds,
/// so the activation flow, its tests, and the tenant-deletion ride-along run
/// unchanged before a real mail server exists. Swapped for the real provider by
/// configuration when one does.
/// </summary>
public class NullMailboxProvider(ILogger<NullMailboxProvider> logger) : IMailboxProvider
{
    public Task CreateMailboxAsync(string domain, string primaryAddress)
    {
        logger.LogDebug("NullMailboxProvider: skipping mailbox creation for {domain} ({address})", domain, primaryAddress);
        return Task.CompletedTask;
    }

    public Task SetEncryptionKeyAsync(string domain, string publicCertificateArmored)
    {
        logger.LogDebug("NullMailboxProvider: skipping encryption key for {domain}", domain);
        return Task.CompletedTask;
    }

    public Task SetDkimKeyAsync(string domain, DkimKey key)
    {
        logger.LogDebug("NullMailboxProvider: skipping DKIM key {selector} for {domain}", key.Selector, domain);
        return Task.CompletedTask;
    }

    public Task SetAliasesAsync(string domain, IReadOnlyCollection<string> localParts)
    {
        logger.LogDebug("NullMailboxProvider: skipping {count} alias(es) for {domain}", localParts.Count, domain);
        return Task.CompletedTask;
    }

    public Task DeleteMailboxAsync(string domain)
    {
        logger.LogDebug("NullMailboxProvider: skipping mailbox deletion for {domain}", domain);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Generates a plausible credential so the flow — and anything reading it back off the
    /// drive — behaves the same shape it will against a real mail server. Nothing can
    /// authenticate with it, because there is no mail server here.
    /// </summary>
    public Task<AppPasswordProvision> ProvisionAppPasswordAsync(string domain, string primaryAddress, string label)
    {
        logger.LogDebug("NullMailboxProvider: issuing a local-only app password '{label}' for {domain}", label, domain);
        return Task.FromResult(new AppPasswordProvision(Guid.NewGuid().ToString("N"), GenerateAppPassword()));
    }

    public Task RevokeAppPasswordAsync(string domain, string appPasswordId)
    {
        logger.LogDebug("NullMailboxProvider: skipping app password revoke {id} for {domain}", appPasswordId, domain);
        return Task.CompletedTask;
    }

    public Task<MailboxStatus?> GetMailboxStatusAsync(string domain)
    {
        logger.LogDebug("NullMailboxProvider: no mailbox status to report for {domain}", domain);
        return Task.FromResult<MailboxStatus?>(null);
    }

    // 20 random bytes as 4 blocks of 5 base32 chars - typed into a mail client once
    private static string GenerateAppPassword()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var random = ByteArrayUtil.GetRndByteArray(20);
        var chars = random.Select(b => alphabet[b % 32]).ToArray();
        return string.Join("-", Enumerable.Range(0, 4).Select(i => new string(chars, i * 5, 5)));
    }
}
