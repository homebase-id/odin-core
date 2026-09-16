#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Util;

namespace Odin.Services.Authorization.BundleTokens;

/// <summary>
/// Issues and manages bundle tokens: one client token mapped to several registered apps.
/// </summary>
/// <remarks>
/// The key chain reuses the V1 client-token code unchanged.  <c>ExchangeGrantService.CreateClientAccessToken</c>
/// splits a client key, escrows the shared secret and -- in the slot V1 uses for the app's key-store key --
/// a random <i>bundle key</i>.  Each member app's key-store key is then stored encrypted with the bundle
/// key, so client half -> client key -> bundle key -> each app's key-store key.
/// <para>
/// Stored in dedicated tables, not <c>ClientRegistrations</c>: every V1 client lookup reads that table by
/// token id and ignores the record type, so keeping bundle tokens out of it is what makes V1 refuse them.
/// </para>
/// </remarks>
public class BundleTokenService(
    IdentityDatabase db,
    ExchangeGrantService exchangeGrantService,
    IAppRegistrationService appRegistrationService,
    OdinContextCache contextCache,
    ITenantLevel2Cache<BundleTokenService> level2Cache)
{
    public const int MaxAppsPerToken = 16;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);
    private static readonly TimeSpan ExchangeWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Issues a bundle token.  The caller holds the only copy of the returned token.
    /// </summary>
    public async Task<ClientAccessToken> IssueAsync(IssueBundleTokenRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));

        if (request.PrimaryAppId == Guid.Empty)
        {
            throw new OdinClientException("A primary app is required", OdinClientErrorCode.ArgumentError);
        }

        if (string.IsNullOrWhiteSpace(request.FriendlyName))
        {
            throw new OdinClientException("A friendly name is required", OdinClientErrorCode.ArgumentError);
        }

        var appIds = new List<Guid> { request.PrimaryAppId };
        appIds.AddRange((request.AppIds ?? []).Where(id => id != Guid.Empty && id != request.PrimaryAppId));
        appIds = appIds.Distinct().ToList();

        if (appIds.Count > MaxAppsPerToken)
        {
            throw new OdinClientException($"A token can reach at most {MaxAppsPerToken} apps", OdinClientErrorCode.ArgumentError);
        }

        var registrations = new List<AppRegistration>();
        foreach (var appId in appIds)
        {
            var record = await db.AppRegistrations.GetAsync(appId);
            if (record == null)
            {
                throw new OdinClientException($"App {appId} is not registered", OdinClientErrorCode.AppNotRegistered);
            }

            var registration = AppRegistrationService.FromRecord(record);
            if (registration.AppKeyStore.IsRevoked)
            {
                throw new OdinClientException($"App '{registration.Name}' is revoked", OdinClientErrorCode.AppRevoked);
            }

            registrations.Add(registration);
        }

        AssertRedirectAllowed(registrations[0], request.RedirectUri);

        var masterKey = odinContext.Caller.GetMasterKey();
        var bundleKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();

        try
        {
            var (accessRegistration, token) = await exchangeGrantService.CreateClientAccessToken(bundleKey, ClientTokenType.AppBundle);

            var now = UnixTimeUtc.Now();
            await using var tx = await db.BeginStackedTransactionAsync();

            await db.BundleTokens.InsertAsync(new BundleTokensRecord
            {
                tokenId = token.Id,
                primaryAppId = request.PrimaryAppId,
                friendlyName = request.FriendlyName.Trim(),
                accessRegistrationJson = OdinSystemSerializer.Serialize(accessRegistration),
                expiresAt = now.AddMilliseconds((long)Lifetime.TotalMilliseconds)
            });

            foreach (var registration in registrations)
            {
                var keyStoreKey = registration.AppKeyStore.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
                try
                {
                    await db.BundleTokenApps.InsertAsync(new BundleTokenAppsRecord
                    {
                        tokenId = token.Id,
                        appId = registration.AppId,
                        encryptedKeyStoreKeyJson = OdinSystemSerializer.Serialize(new SymmetricKeyEncryptedAes(bundleKey, keyStoreKey))
                    });
                }
                finally
                {
                    keyStoreKey.Wipe();
                }
            }

            tx.Commit();
            return token;
        }
        finally
        {
            bundleKey.Wipe();
        }
    }

    /// <summary>
    /// Issues a token and seals it for the client with ECDH, exactly as YouAuth does; the client collects
    /// it once, within five minutes, through <see cref="ExchangeAsync"/>.
    /// </summary>
    public async Task<BeginBundleTokenExchangeResponse> BeginExchangeAsync(BeginBundleTokenExchangeRequest request,
        IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.JwkBase64UrlPublicKey, nameof(request.JwkBase64UrlPublicKey));

        EccPublicKeyData remotePublicKey;
        try
        {
            remotePublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(request.JwkBase64UrlPublicKey);
        }
        catch (Exception)
        {
            throw new OdinClientException("The public key is not a valid JWK", OdinClientErrorCode.ArgumentError);
        }

        var token = await IssueAsync(request, odinContext);

        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);
        var exchangeSalt = ByteArrayUtil.GetRndByteArray(16);

        var exchangeSharedSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKey, exchangeSalt);
        var digest = SHA256.HashData(exchangeSharedSecret.GetKey()).ToBase64();

        var (sharedSecretIv, sharedSecretCipher) = AesCbc.Encrypt(token.SharedSecret.GetKey(), exchangeSharedSecret);
        var (clientAuthTokenIv, clientAuthTokenCipher) = AesCbc.Encrypt(token.ToAuthenticationToken().ToPortableBytes(), exchangeSharedSecret);

        await level2Cache.SetAsync(ExchangeCacheKey(digest),
            new EncryptedTokenExchange(sharedSecretCipher, sharedSecretIv, clientAuthTokenCipher, clientAuthTokenIv),
            ExchangeWindow);

        return new BeginBundleTokenExchangeResponse
        {
            TokenId = token.Id,
            ExchangePublicKeyJwkBase64Url = keyPair.PublicKeyJwkBase64Url(),
            ExchangeSalt64 = Convert.ToBase64String(exchangeSalt)
        };
    }

    /// <summary>The sealed token for <paramref name="secretDigest"/>, once; null when unknown or collected.</summary>
    public async Task<EncryptedTokenExchange?> ExchangeAsync(string secretDigest)
    {
        if (string.IsNullOrWhiteSpace(secretDigest))
        {
            return null;
        }

        var key = ExchangeCacheKey(secretDigest);
        var cached = await level2Cache.TryGetAsync<EncryptedTokenExchange?>(key);
        if (!cached.HasValue)
        {
            return null;
        }

        await level2Cache.RemoveAsync(key);
        return cached.Value;
    }

    public async Task<List<RedactedBundleToken>> GetTokensAsync(Guid? appId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var tokens = await db.BundleTokens.GetAllAsync();
        var members = await db.BundleTokenApps.GetAllAsync();
        var apps = (await appRegistrationService.GetRegisteredAppsAsync(odinContext)).ToDictionary(a => a.AppId.Value);

        var byToken = members.GroupBy(m => m.tokenId).ToDictionary(g => g.Key, g => g.ToList());

        return tokens
            .Select(t => ToRedacted(t, byToken.GetValueOrDefault(t.tokenId) ?? [], apps))
            .Where(t => appId == null || t.Apps.Any(a => a.AppId == appId))
            .OrderByDescending(t => t.Created.milliseconds)
            .ToList();
    }

    public async Task SetRevokedAsync(Guid tokenId, bool revoked, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var record = await GetRecordOrThrowAsync(tokenId);
        var accessRegistration = OdinSystemSerializer.DeserializeOrThrow<ServerHalfOfClientKey>(record.accessRegistrationJson);
        if (accessRegistration.IsRevoked == revoked)
        {
            return;
        }

        accessRegistration.IsRevoked = revoked;
        record.accessRegistrationJson = OdinSystemSerializer.Serialize(accessRegistration);
        await db.BundleTokens.UpdateAsync(record);

        // Cached contexts are keyed by the client half, which the server never holds, so a single
        // entry cannot be targeted.  Revocation has to take effect now, not when the entry expires.
        await contextCache.ResetAsync();
    }

    public async Task DeleteAsync(Guid tokenId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        await GetRecordOrThrowAsync(tokenId);
        await DeleteInternalAsync(tokenId);
    }

    /// <summary>Removes one app from a token.  The primary app cannot be removed; delete the token instead.</summary>
    public async Task RemoveAppAsync(Guid tokenId, Guid appId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var record = await GetRecordOrThrowAsync(tokenId);
        if (record.primaryAppId == appId)
        {
            throw new OdinClientException("The primary app cannot be removed; delete the token instead",
                OdinClientErrorCode.ArgumentError);
        }

        if (await db.BundleTokenApps.DeleteAsync(tokenId, appId) == 0)
        {
            throw new OdinClientException("The app is not part of this token", OdinClientErrorCode.UnknownId);
        }

        await contextCache.ResetAsync();
    }

    /// <summary>Logout: deletes the bundle token the caller is using.</summary>
    public async Task DeleteCurrentAsync(IOdinContext odinContext)
    {
        var tokenId = odinContext.Caller.OdinClientContext?.AccessRegistrationId;
        if (tokenId == null || await db.BundleTokens.GetAsync(tokenId) == null)
        {
            throw new OdinClientException("The caller is not using a bundle token", OdinClientErrorCode.InvalidAccessRegistrationId);
        }

        await DeleteInternalAsync(tokenId);
    }

    //

    private async Task DeleteInternalAsync(Guid tokenId)
    {
        await using (var tx = await db.BeginStackedTransactionAsync())
        {
            await db.BundleTokenApps.DeleteByTokenIdAsync(tokenId);
            await db.BundleTokens.DeleteAsync(tokenId);
            tx.Commit();
        }

        await contextCache.ResetAsync();
    }

    private async Task<BundleTokensRecord> GetRecordOrThrowAsync(Guid tokenId)
    {
        return await db.BundleTokens.GetAsync(tokenId)
               ?? throw new OdinClientException("No such bundle token", OdinClientErrorCode.UnknownId);
    }

    /// <summary>
    /// When the primary app declares a CORS host, the token may only be handed to a page on that host.
    /// Apps without one (native clients with custom URL schemes) are not restricted; the owner sees the
    /// redirect on the consent screen either way.
    /// </summary>
    private static void AssertRedirectAllowed(AppRegistration primary, string? redirectUri)
    {
        var problem = RedirectProblem(primary.CorsHostName, redirectUri);
        if (problem != null)
        {
            throw new OdinClientException(problem, OdinClientErrorCode.InvalidCorsHostName);
        }
    }

    /// <summary>Why <paramref name="redirectUri"/> may not receive a token for an app on <paramref name="corsHostName"/>; null when it may.</summary>
    public static string? RedirectProblem(string? corsHostName, string? redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri) || string.IsNullOrWhiteSpace(corsHostName))
        {
            return null;
        }

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return "The redirect is not a valid absolute URI";
        }

        var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return string.Equals(authority, corsHostName, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"The redirect must go to '{corsHostName}', the primary app's host";
    }

    private static RedactedBundleToken ToRedacted(BundleTokensRecord record, List<BundleTokenAppsRecord> members,
        Dictionary<Guid, RedactedAppRegistration> apps)
    {
        var accessRegistration = OdinSystemSerializer.Deserialize<ServerHalfOfClientKey>(record.accessRegistrationJson);

        return new RedactedBundleToken
        {
            TokenId = record.tokenId,
            FriendlyName = record.friendlyName,
            PrimaryAppId = record.primaryAppId,
            IsRevoked = accessRegistration?.IsRevoked ?? true,
            Created = record.created,
            ExpiresAt = record.expiresAt,
            Apps = members
                .Select(m =>
                {
                    apps.TryGetValue(m.appId, out var app);
                    return new BundleTokenAppInfo
                    {
                        AppId = m.appId,
                        Name = app?.Name ?? "",
                        AppSlug = app?.AppSlug ?? "",
                        IsPrimary = m.appId == record.primaryAppId,
                        IsRevoked = app?.IsRevoked ?? false,
                        IsMissing = app == null
                    };
                })
                .OrderByDescending(a => a.IsPrimary)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static string ExchangeCacheKey(string digest) => $"BundleTokenExchange:{digest}";
}
