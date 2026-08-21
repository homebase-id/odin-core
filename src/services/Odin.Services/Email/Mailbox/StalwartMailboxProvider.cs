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

    public async Task SetEncryptionKeyAsync(string domain, string publicCertificateArmored)
    {
        var (accountId, _) = await RequireUserAccountAsync(domain);

        // Child objects are addressed in the USER's JMAP account context
        var keyId = await SetAsync("x:PublicKey", create: new JsonObject
        {
            ["key"] = publicCertificateArmored,
            ["description"] = "Homebase E2E email certificate",
        }, jmapAccountId: accountId);

        // Point encryption-at-rest at the fresh key; older key objects stay behind
        // harmlessly (mail already stored was encrypted to them)
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
        logger.LogInformation("Stalwart encryption-at-rest enabled for {domain} (key {keyId})", domain, keyId);
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

    public async Task<string> ProvisionAppPasswordAsync(string domain, string primaryAddress, string label)
    {
        var (accountId, _) = await RequireUserAccountAsync(domain);

        // secret is serverSet: Stalwart generates it and returns it exactly once
        var response = await CallAsync("x:AppPassword/set", new JsonObject
        {
            ["accountId"] = accountId,
            ["create"] = new JsonObject { ["c1"] = new JsonObject { ["description"] = label } },
        });
        ThrowOnFailedSet(response, "x:AppPassword");

        var secret = response["created"]?["c1"]?["secret"]?.GetValue<string>();
        if (string.IsNullOrEmpty(secret))
        {
            throw new OdinSystemException("Stalwart did not return an app password secret");
        }

        logger.LogInformation("Stalwart app password '{label}' provisioned for {domain}", label, domain);
        return secret;
    }

    //
    // Registry lookups (idempotency primitives)
    //

    private async Task<string> EnsureDomainAsync(string domain)
    {
        return await FindDomainIdAsync(domain)
               ?? await SetAsync("x:Domain", create: new JsonObject { ["name"] = domain });
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

    private async Task<JsonNode> CallAsync(string method, JsonObject args)
    {
        var body = new JsonObject
        {
            ["using"] = new JsonArray(CoreCapability, ManagementCapability),
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
