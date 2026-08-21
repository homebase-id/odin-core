using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
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

    public Task<string> ProvisionAppPasswordAsync(string domain, string primaryAddress, string label)
    {
        logger.LogDebug("NullMailboxProvider: generating app password '{label}' for {domain} (no mail server to install it on)", label, domain);
        return Task.FromResult(GenerateAppPassword());
    }

    // 20 random bytes as 4 blocks of 5 base32 chars - typed into mail clients once
    private static string GenerateAppPassword()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var random = ByteArrayUtil.GetRndByteArray(20);
        var chars = random.Select(b => alphabet[b % 32]).ToArray();
        return string.Join("-", Enumerable.Range(0, 4).Select(i => new string(chars, i * 5, 5)));
    }
}
