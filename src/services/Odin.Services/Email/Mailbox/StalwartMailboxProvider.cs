using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Services.Configuration;
using Odin.Services.Email.Dkim;

namespace Odin.Services.Email.Mailbox;

#nullable enable

/// <summary>
/// The real mail-server provider, speaking Stalwart's JMAP-based management
/// registry (v0.16). The wire contract was live-verified against a dev instance
/// before this was written - see docs/stalwart-admin-api-notes.md for the
/// payload shapes, the variant "@type" convention, serverSet fields, and the
/// numeric-string-keyed List serialization, all of which this class encodes.
///
/// One mailbox per tenant domain. All operations are idempotent (activation
/// re-runs freely): creates look up first, updates converge.
/// </summary>
public class StalwartMailboxProvider(
    ILogger<StalwartMailboxProvider> logger,
    IDynamicHttpClientFactory httpClientFactory,
    OdinConfiguration configuration) : IMailboxProvider
{
    private const string ManagementCapability = "urn:stalwart:jmap";
    private const string CoreCapability = "urn:ietf:params:jmap:core";

    // Mailbox counts are standard JMAP Mail, not a Stalwart extension - so this part of the
    // status survives a change of mail server.
    private const string MailCapability = "urn:ietf:params:jmap:mail";

    private string? _adminAccountId;

    //

    public async Task CreateMailboxAsync(string domain, string primaryAddress)
    {
        var localPart = LocalPartOf(primaryAddress, domain);
        var domainId = await EnsureDomainAsync(domain);

        var account = await FindUserAccountAsync(domainId);
        if (account != null)
        {
            logger.LogDebug("Stalwart mailbox for {domain} already exists (account {id})", domain, account.Value.id);
            return;
        }

        var created = await SetAsync("x:Account", create: new JsonObject
        {
            ["@type"] = "User",
            ["name"] = localPart,
            ["domainId"] = domainId,
        });
        logger.LogInformation("Stalwart mailbox created for {domain} (account {id})", domain, created);
    }

    /// <summary>
    /// Uploads the E2E public certificate and points encryption-at-rest at it.
    ///
    /// Old key objects are REMOVED rather than left behind. Stalwart caps public keys per account
    /// (maxPublicKeys, 5 by default) and every rotation would otherwise consume one, so a handful
    /// of rotations bricks the endpoint with "overQuota" — which is exactly what happened in
    /// testing. Removing them is safe: the public key only decides what NEW mail is encrypted to,
    /// and already-stored mail is decrypted client-side with the private half, which never leaves
    /// the email drive.
    ///
    /// Re-running with the same certificate is a no-op, so activation stays idempotent.
    /// </summary>
    public async Task SetEncryptionKeyAsync(string domain, string publicCertificateArmored)
    {
        var (accountId, _) = await RequireUserAccountAsync(domain);

        // Child objects are addressed in the USER's JMAP account context
        var existing = await GetAsync("x:PublicKey", jmapAccountId: accountId);
        var alreadyThere = existing.FirstOrDefault(k =>
            k?["key"]?.GetValue<string>() == publicCertificateArmored);

        var keyId = alreadyThere?["id"]?.GetValue<string>();
        if (keyId == null)
        {
            // Make room first: a full quota would fail the create below.
            await PruneEncryptionKeysAsync(accountId, existing, keepId: null);

            keyId = await SetAsync("x:PublicKey", create: new JsonObject
            {
                ["key"] = publicCertificateArmored,
                ["description"] = "Homebase E2E email certificate",
            }, jmapAccountId: accountId);
        }

        await SetAsync("x:Account", updateId: accountId, update: new JsonObject
        {
            ["encryptionAtRest"] = new JsonObject
            {
                ["@type"] = "Aes256",
                ["publicKey"] = keyId,
                ["encryptOnAppend"] = true,
                ["allowSpamTraining"] = false,
            }
        });

        // Now that nothing references them, drop anything that is not the key in use.
        await PruneEncryptionKeysAsync(accountId, await GetAsync("x:PublicKey", jmapAccountId: accountId), keepId: keyId);

        logger.LogInformation("Stalwart encryption-at-rest enabled for {domain} (key {keyId})", domain, keyId);
    }

    /// <summary>
    /// Removes public key objects, optionally keeping one. Best-effort: failing to tidy up is not
    /// a reason to fail the caller, which has already done the part that matters.
    /// </summary>
    private async Task PruneEncryptionKeysAsync(string accountId, List<JsonNode> keys, string? keepId)
    {
        foreach (var key in keys)
        {
            var id = key?["id"]?.GetValue<string>();
            if (id == null || id == keepId)
            {
                continue;
            }

            try
            {
                await SetAsync("x:PublicKey", destroyId: id, jmapAccountId: accountId);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Stalwart would not remove the old public key {id}", id);
            }
        }
    }

    public async Task SetDkimKeyAsync(string domain, DkimKey key)
    {
        var domainId = await EnsureDomainAsync(domain);
        var privateKeyPem = new string(System.Security.Cryptography.PemEncoding.Write("PRIVATE KEY", key.PrivateKeyPkcs8));
        var variant = key.Algorithm == DkimAlgorithm.Ed25519 ? "Dkim1Ed25519Sha256" : "Dkim1RsaSha256";
        var privateKeyValue = new JsonObject { ["@type"] = "Text", ["secret"] = privateKeyPem };

        var existing = (await GetAsync("x:DkimSignature"))
            .FirstOrDefault(x => x["domainId"]?.GetValue<string>() == domainId &&
                                 x["selector"]?.GetValue<string>() == key.Selector);
        if (existing != null)
        {
            // Rotation: same selector, new key material
            await SetAsync("x:DkimSignature", updateId: existing["id"]!.GetValue<string>(), update: new JsonObject
            {
                ["privateKey"] = privateKeyValue,
            });
            logger.LogInformation("Stalwart DKIM key rotated for {domain} selector {selector}", domain, key.Selector);
            return;
        }

        await SetAsync("x:DkimSignature", create: new JsonObject
        {
            ["@type"] = variant,
            ["domainId"] = domainId,
            ["selector"] = key.Selector,
            ["privateKey"] = privateKeyValue,
        });
        logger.LogInformation("Stalwart DKIM key installed for {domain} selector {selector} ({kTag})", domain, key.Selector, key.KTag);
    }

    public async Task SetAliasesAsync(string domain, IReadOnlyCollection<string> localParts)
    {
        var domainId = await EnsureDomainAsync(domain);
        var (accountId, _) = await RequireUserAccountAsync(domain);

        // Registry List<T> serializes as a map with numeric string keys
        var aliases = new JsonObject();
        var index = 0;
        foreach (var localPart in localParts)
        {
            aliases[index.ToString()] = new JsonObject
            {
                ["name"] = localPart,
                ["domainId"] = domainId,
                ["enabled"] = true,
            };
            index++;
        }

        await SetAsync("x:Account", updateId: accountId, update: new JsonObject { ["aliases"] = aliases });
        logger.LogInformation("Stalwart aliases set for {domain}: {count}", domain, localParts.Count);
    }

    public async Task DeleteMailboxAsync(string domain)
    {
        var domainId = await FindDomainIdAsync(domain);
        if (domainId == null)
        {
            logger.LogDebug("Stalwart has no domain object for {domain}; nothing to delete", domain);
            return;
        }

        foreach (var account in await GetAsync("x:Account"))
        {
            if (account["domainId"]?.GetValue<string>() == domainId)
            {
                await SetAsync("x:Account", destroyId: account["id"]!.GetValue<string>());
            }
        }

        foreach (var signature in await GetAsync("x:DkimSignature"))
        {
            if (signature["domainId"]?.GetValue<string>() == domainId)
            {
                await SetAsync("x:DkimSignature", destroyId: signature["id"]!.GetValue<string>());
            }
        }

        await SetAsync("x:Domain", destroyId: domainId);
        logger.LogInformation("Stalwart mailbox, DKIM keys and domain deleted for {domain}", domain);
    }

    public async Task<AppPasswordProvision> ProvisionAppPasswordAsync(string domain, string primaryAddress, string label)
    {
        var (accountId, _) = await RequireUserAccountAsync(domain);

        // secret is serverSet: Stalwart generates it and returns it exactly once. The id comes
        // back in the same response and is the only handle a later revoke has, so both are kept.
        var response = await CallAsync("x:AppPassword/set", new JsonObject
        {
            ["accountId"] = accountId,
            ["create"] = new JsonObject { ["c1"] = new JsonObject { ["description"] = label } },
        });
        ThrowOnFailedSet(response, "x:AppPassword");

        var created = response["created"]?["c1"];
        var secret = created?["secret"]?.GetValue<string>();
        var id = created?["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(id))
        {
            throw new OdinSystemException("Stalwart did not return an app password id and secret");
        }

        logger.LogInformation("Stalwart app password '{label}' provisioned for {domain}", label, domain);
        return new AppPasswordProvision(id, secret);
    }

    /// <summary>
    /// Destroys the credential. Live-verified idempotency: destroying an id Stalwart no longer
    /// knows answers <c>notDestroyed: {id: {type: "notFound"}}</c> rather than failing the request,
    /// so that one reason is treated as success and every other reason still throws.
    ///
    /// Deliberately NOT a read-then-destroy: the extra round trip buys nothing and leaves a window
    /// in which someone else revokes the credential between the two calls.
    /// </summary>
    public async Task RevokeAppPasswordAsync(string domain, string appPasswordId)
    {
        var (accountId, _) = await RequireUserAccountAsync(domain);

        var response = await CallAsync("x:AppPassword/set", new JsonObject
        {
            ["accountId"] = accountId,
            ["destroy"] = new JsonArray(appPasswordId),
        });

        if (response["notDestroyed"] is JsonObject failures && failures.Count > 0)
        {
            foreach (var (id, reason) in failures)
            {
                var type = reason?["type"]?.GetValue<string>();
                if (type == "notFound")
                {
                    logger.LogDebug("Stalwart app password {id} was already gone for {domain}", id, domain);
                    continue;
                }

                throw new OdinSystemException($"Stalwart x:AppPassword notDestroyed: {failures.ToJsonString()}");
            }

            return;
        }

        logger.LogInformation("Stalwart app password {id} revoked for {domain}", appPasswordId, domain);
    }

    /// <summary>
    /// Reads the account's disk usage. Live-verified: the User account carries a serverSet
    /// <c>usedDiskQuota</c> in bytes and a mutable <c>quotas</c> map whose <c>maxDiskQuota</c> key
    /// is the limit; an unset limit simply means unlimited.
    ///
    /// Never throws — this feeds one line on a status screen, so a mail server that cannot answer
    /// must degrade to "not shown" rather than take the screen down with it.
    /// </summary>
    public async Task<MailboxStatus?> GetMailboxStatusAsync(string domain)
    {
        try
        {
            var domainId = await FindDomainIdAsync(domain);
            if (domainId == null)
            {
                return null;
            }

            var accounts = await GetAsync("x:Account");
            var account = accounts.FirstOrDefault(a =>
                a?["@type"]?.GetValue<string>() == "User" &&
                a?["domainId"]?.GetValue<string>() == domainId);

            if (account == null)
            {
                return null;
            }

            var accountId = account["id"]?.GetValue<string>();
            var used = (account["usedDiskQuota"] as JsonValue)?.GetValue<long>() ?? 0;
            long? quota = (account["quotas"]?["maxDiskQuota"] as JsonValue)?.GetValue<long>();

            var (inboxTotal, inboxUnread, junkTotal) = await ReadMailboxCountsAsync(accountId);
            var queued = await ReadQueueDepthAsync();

            return new MailboxStatus(used, quota, inboxTotal, inboxUnread, junkTotal, queued);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Stalwart could not report mailbox status for {domain}", domain);
            return null;
        }
    }

    /// <summary>
    /// Per-mailbox counts, read as the admin against the USER's account — no user password is
    /// involved. Standard JMAP Mail, so this is not Stalwart-specific.
    ///
    /// Mailboxes are matched on their ROLE rather than their name: the display names are
    /// localised ("Junk Mail" here, something else elsewhere), the roles are not.
    /// </summary>
    private async Task<(int InboxTotal, int InboxUnread, int JunkTotal)> ReadMailboxCountsAsync(string? accountId)
    {
        if (string.IsNullOrEmpty(accountId))
        {
            return (0, 0, 0);
        }

        var response = await CallAsync(
            "Mailbox/get",
            new JsonObject { ["accountId"] = accountId },
            MailCapability);

        var mailboxes = response["list"] as JsonArray;
        if (mailboxes == null)
        {
            return (0, 0, 0);
        }

        var inbox = mailboxes.FirstOrDefault(m => m?["role"]?.GetValue<string>() == "inbox");
        var junk = mailboxes.FirstOrDefault(m => m?["role"]?.GetValue<string>() == "junk");

        return (
            (inbox?["totalEmails"] as JsonValue)?.GetValue<int>() ?? 0,
            (inbox?["unreadEmails"] as JsonValue)?.GetValue<int>() ?? 0,
            (junk?["totalEmails"] as JsonValue)?.GetValue<int>() ?? 0);
    }

    /// <summary>
    /// Outbound messages still waiting. Server-wide rather than per-tenant — the queue does not
    /// carry a domain to filter on — so treat it as "this host is having trouble sending",
    /// which is what it is.
    /// </summary>
    private async Task<int> ReadQueueDepthAsync()
    {
        var queued = await GetAsync("x:QueuedMessage");
        return queued.Count;
    }

    //
    // Registry lookups (idempotency primitives)
    //

    /// <summary>
    /// Domains are created with MANUAL DKIM management.
    ///
    /// Stalwart's default is Automatic, which mints its own keypair the moment a domain
    /// exists - selectors from a "v{version}-{algorithm}-{date}" template - and ROTATES it on
    /// a 90-day timer. We publish DNS only for the s1/s2 pair we generate, so those keys sign
    /// mail nothing can verify: Gmail reported `dkim=permerror (no key for signature)` on real
    /// tenant mail.
    ///
    /// Prevented here rather than cleaned up afterwards, because rotation makes cleanup a
    /// losing game: delete the keys today and Automatic management mints replacements in
    /// ninety days, on a schedule nobody is watching.
    ///
    /// Only applied at CREATE. A domain that already exists keeps whatever it was created
    /// with - relevant only for domains predating this, which is a one-off fix rather than
    /// something worth carrying reconciliation code for.
    /// </summary>
    private async Task<string> EnsureDomainAsync(string domain)
    {
        var existing = await FindDomainIdAsync(domain);
        if (existing != null)
        {
            return existing;
        }

        return await SetAsync("x:Domain", create: new JsonObject
        {
            ["name"] = domain,
            ["dkimManagement"] = new JsonObject { ["@type"] = "Manual" },
        });
    }

    private async Task<string?> FindDomainIdAsync(string domain)
    {
        return (await GetAsync("x:Domain"))
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), domain, StringComparison.OrdinalIgnoreCase))
            ?["id"]?.GetValue<string>();
    }

    private async Task<(string id, string name)?> FindUserAccountAsync(string domainId)
    {
        var account = (await GetAsync("x:Account"))
            .FirstOrDefault(x => x["domainId"]?.GetValue<string>() == domainId);
        return account == null
            ? null
            : (account["id"]!.GetValue<string>(), account["name"]?.GetValue<string>() ?? "");
    }

    private async Task<(string id, string name)> RequireUserAccountAsync(string domain)
    {
        var domainId = await FindDomainIdAsync(domain)
                       ?? throw new OdinSystemException($"Stalwart has no domain object for {domain}; create the mailbox first");
        return await FindUserAccountAsync(domainId)
               ?? throw new OdinSystemException($"Stalwart has no mailbox account for {domain}; create the mailbox first");
    }

    private static string LocalPartOf(string primaryAddress, string domain)
    {
        var parts = primaryAddress.Split('@', 2);
        if (parts.Length != 2 || !string.Equals(parts[1], domain, StringComparison.OrdinalIgnoreCase) || parts[0].Length == 0)
        {
            throw new OdinSystemException($"Primary address '{primaryAddress}' is not an address at {domain}");
        }
        return parts[0];
    }

    //
    // JMAP transport
    //

    private async Task<List<JsonNode>> GetAsync(string objectType, string? jmapAccountId = null)
    {
        var response = await CallAsync($"{objectType}/get", new JsonObject
        {
            ["accountId"] = jmapAccountId ?? await AdminAccountIdAsync(),
        });
        return (response["list"] as JsonArray)?.OfType<JsonNode>().ToList() ?? [];
    }

    /// <summary>One create/update/destroy against a registry type; returns the created id when creating.</summary>
    private async Task<string> SetAsync(
        string objectType,
        JsonObject? create = null,
        string? updateId = null,
        JsonObject? update = null,
        string? destroyId = null,
        string? jmapAccountId = null)
    {
        var args = new JsonObject { ["accountId"] = jmapAccountId ?? await AdminAccountIdAsync() };
        if (create != null)
        {
            args["create"] = new JsonObject { ["c1"] = create };
        }
        if (updateId != null)
        {
            args["update"] = new JsonObject { [updateId] = update };
        }
        if (destroyId != null)
        {
            args["destroy"] = new JsonArray(destroyId);
        }

        var response = await CallAsync($"{objectType}/set", args);
        ThrowOnFailedSet(response, objectType);
        return response["created"]?["c1"]?["id"]?.GetValue<string>() ?? "";
    }

    private static void ThrowOnFailedSet(JsonNode response, string objectType)
    {
        foreach (var section in new[] { "notCreated", "notUpdated", "notDestroyed" })
        {
            if (response[section] is JsonObject failures && failures.Count > 0)
            {
                throw new OdinSystemException($"Stalwart {objectType} {section}: {failures.ToJsonString()}");
            }
        }
    }

    private async Task<string> AdminAccountIdAsync()
    {
        if (_adminAccountId != null)
        {
            return _adminAccountId;
        }

        var session = JsonNode.Parse(await SendAsync(HttpMethod.Get, "/jmap/session", body: null))
                     ?? throw new OdinSystemException("Stalwart returned an empty JMAP session");
        var primaryAccounts = session["primaryAccounts"] as JsonObject;
        _adminAccountId = (primaryAccounts?[ManagementCapability] ?? primaryAccounts?.FirstOrDefault().Value)?.GetValue<string>()
                          ?? throw new OdinSystemException("Stalwart JMAP session has no primary account");
        return _adminAccountId;
    }

    private async Task<JsonNode> CallAsync(string method, JsonObject args, string? capability = null)
    {
        var body = new JsonObject
        {
            ["using"] = new JsonArray(CoreCapability, capability ?? ManagementCapability),
            ["methodCalls"] = new JsonArray(new JsonArray(method, args, "0")),
        };

        var raw = JsonNode.Parse(await SendAsync(HttpMethod.Post, "/jmap", body.ToJsonString()))
                  ?? throw new OdinSystemException("Stalwart returned an empty JMAP response");

        var methodResponse = (raw["methodResponses"] as JsonArray)?.FirstOrDefault() as JsonArray
                             ?? throw new OdinSystemException($"Stalwart returned no response for {method}");
        var responseName = methodResponse[0]?.GetValue<string>();
        var responseArgs = methodResponse[1] ?? new JsonObject();

        if (responseName == "error")
        {
            throw new OdinSystemException($"Stalwart {method} failed: {responseArgs.ToJsonString()}");
        }

        return responseArgs;
    }

    private async Task<string> SendAsync(HttpMethod method, string path, string? body)
    {
        var stalwart = configuration.Email.Stalwart;
        var httpClient = httpClientFactory.CreateClient($"{nameof(StalwartMailboxProvider)}");

        using var request = new HttpRequestMessage(method, $"{stalwart.BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{stalwart.AdminUsername}:{stalwart.AdminPassword}")));
        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new OdinSystemException($"Stalwart {method} {path} returned {(int)response.StatusCode}: {content}");
        }

        return content;
    }
}
