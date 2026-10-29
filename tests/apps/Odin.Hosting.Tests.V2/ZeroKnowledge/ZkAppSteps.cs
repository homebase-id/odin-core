#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.Tests.V2.YouAuth;
using Odin.Services.Authentication.YouAuth;
using Odin.Hosting.Tests.YouAuthApi;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// What the owner console's browser and an app do with a zero-knowledge app key (docs/zero-knowledge-drive-api-plan.md,
/// step 3): converting Vault while it keeps its key, and an app's sign-in that asks for its key.
/// </summary>
internal static class ZkAppSteps
{
    public static readonly TargetDrive Vault = WellKnownAppDrives.VaultDrive;
    public static Guid VaultId => Vault.Alias.Value;

    public sealed record Converted(byte[] DriveKey, byte[] WriteOnlyPrivateKey, Dictionary<Guid, byte[]> AppKeys);

    /// <summary>The browser converts Vault: its key re-locked with the zero-knowledge master key, a copy for each app.</summary>
    public static Task<Converted> ConvertVaultAsync(OwnerSession owner, byte[] zkMasterKey) =>
        ConvertDriveAsync(owner, zkMasterKey, VaultId);

    public static async Task<Converted> ConvertDriveAsync(OwnerSession owner, byte[] zkMasterKey, Guid driveId)
    {
        var (request, converted) = await MakeConversionAsync(owner, zkMasterKey, driveId: driveId);
        var response = await ZkOwnerSteps.Client(owner).ConvertDrive(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return converted;
    }

    /// <summary>
    /// What the browser sends to convert a drive (Vault unless <paramref name="driveId"/> names another): the drive's key, which the server hands over one last time, locked with
    /// the zero-knowledge master key; a new write-only key pair; and, for every app granted the drive but those
    /// <paramref name="leaveOut"/> names, the key locked with the app's key (made where the app has none).
    /// </summary>
    public static async Task<(ZkConvertDriveKeepingKeyRequest Request, Converted Converted)> MakeConversionAsync(OwnerSession owner,
        byte[] zkMasterKey, Func<Guid, bool>? leaveOut = null, Guid? driveId = null)
    {
        var id = driveId ?? VaultId;
        var client = ZkOwnerSteps.Client(owner);
        var keyResponse = await client.GetDriveKeyForConversion(id);
        Assert.That(keyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), keyResponse.Error?.Content);
        var driveKey = keyResponse.Content!.StorageKey64.FromBase64();

        var appKeys = new Dictionary<Guid, byte[]>();
        var apps = new List<ZkAppKeysRequest>();
        foreach (var app in (await GetAppKeysAsync(owner)).Where(a => a.Drives.Any(d => d.DriveId == id)))
        {
            if (leaveOut?.Invoke(app.AppId) == true)
            {
                continue;
            }

            var (appKey, newLocked) = OpenOrMakeAppKey(app, zkMasterKey);
            appKeys[app.AppId] = appKey;
            apps.Add(new ZkAppKeysRequest
            {
                AppId = app.AppId,
                ZkMasterKeyEncryptedAppKey = newLocked,
                ZkAppKeyEncryptedStorageKeys = { [id] = ZkBrowser.Lock(driveKey, appKey) }
            });
        }

        var writeOnly = ZkBrowser.MakeKeyPair();
        var request = new ZkConvertDriveKeepingKeyRequest
        {
            DriveId = id,
            ZkMasterKeyEncryptedStorageKey = ZkBrowser.Lock(driveKey, zkMasterKey),
            WriteOnlyPublicKeyJwk = writeOnly.PublicKeyJwk,
            ZkStorageKeyEncryptedWriteOnlyPrivateKey = ZkBrowser.Lock(writeOnly.PrivateKeyPkcs8, driveKey),
            Apps = apps
        };

        return (request, new Converted(driveKey, writeOnly.PrivateKeyPkcs8, appKeys));
    }

    public static async Task<List<ZkAppKeysResponse>> GetAppKeysAsync(OwnerSession owner)
    {
        var response = await ZkOwnerSteps.Client(owner).GetAppKeys();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return response.Content!;
    }

    /// <summary>The app's key, opened with the zero-knowledge master key; or a new one, with the locked copy to save.</summary>
    public static (byte[] AppKey, string? NewLocked) OpenOrMakeAppKey(ZkAppKeysResponse app, byte[] zkMasterKey)
    {
        if (app.ZkMasterKeyEncryptedAppKey != null)
        {
            return (ZkBrowser.Open(app.ZkMasterKeyEncryptedAppKey, zkMasterKey), null);
        }

        var appKey = ByteArrayUtil.GetRndByteArray(16);
        return (appKey, ZkBrowser.Lock(appKey, zkMasterKey));
    }

    /// <param name="clientId">The app's id (the default) or its slug, as chat-kmp names it</param>
    public static YouAuthAuthorizeRequest SignInRequest(Guid appId, YouAuthFlow.KeyPair keyPair, bool wantsZkAppKey = true,
        string? clientId = null) => new()
    {
        ClientId = clientId ?? appId.ToString(),
        ClientType = ClientType.app,
        PermissionRequest = OdinSystemSerializer.Serialize(new YouAuthAppParameters
        {
            AppName = "Zero-knowledge test app",
            AppOrigin = "dev.dotyou.cloud:3005",
            AppId = appId.ToString(),
            ClientFriendly = "Test device",
            DrivesParam = "[]",
            Return = "backend-will-decide"
        }),
        PublicKey = keyPair.PublicKeyJwk,
        State = "s",
        RedirectUri = "https://app/authorization/code/callback",
        ZkAppKey = wantsZkAppKey ? "1" : ""
    };

    /// <summary>
    /// An app's sign-in that asks for its zero-knowledge key, with the consent page's part played as the browser does: it
    /// opens (or makes) the app's key, backfills the copy of each zero-knowledge drive the app is granted that has none,
    /// and seals the key to the sign-in's public key.  Returns the new device's session and the key it opened.
    /// </summary>
    public static async Task<(AppSession Device, byte[] AppKey)> SignInAsync(OwnerSession owner, Guid appId, byte[] zkMasterKey,
        string? clientId = null)
    {
        var keyPair = YouAuthFlow.NewKeyPair();
        var request = SignInRequest(appId, keyPair, clientId: clientId);

        var returnUrl = YouAuthFlow.AssertRedirectsToConsent(await YouAuthFlow.AuthorizeAsync(owner.Host, owner, request));
        Assert.That(returnUrl.WantsZkAppKey, Is.True, "the consent page sees what the app asked for");

        var (appKey, locked) = await BackfillAsync(owner, appId, zkMasterKey);
        await YouAuthFlow.GiveConsentAsync(owner.Host, owner, returnUrl, SealToSignIn(appKey, keyPair), locked);

        var (device, token) = await CompleteSignInAsync(owner, appId, keyPair, request);
        Assert.That(token.ZkAppKey, Is.Not.Null.And.Not.Empty, "the token response carries the app's key");
        var opened = ZkBrowser.Unseal(token.ZkAppKey!, Pkcs8(keyPair));
        Assert.That(opened, Is.EqualTo(appKey), "the device opens the key the browser sealed");
        return (device, opened);
    }

    public static string SealToSignIn(byte[] appKey, YouAuthFlow.KeyPair keyPair) =>
        ZkBrowser.Seal(appKey, Base64UrlEncoder.DecodeString(keyPair.PublicKeyJwk));

    /// <summary>
    /// The consent page's backfill: the app's key, made if missing, and a copy of each granted drive's key it lacks.
    /// Returns the key and the locked copy of it the app now holds, which consent names.
    /// </summary>
    public static async Task<(byte[] AppKey, string Locked)> BackfillAsync(OwnerSession owner, Guid appId, byte[] zkMasterKey)
    {
        var app = (await GetAppKeysAsync(owner)).Single(a => a.AppId == appId);
        var (appKey, newLocked) = OpenOrMakeAppKey(app, zkMasterKey);
        var missing = app.Drives.Where(d => d.IsZeroKnowledge && !d.HasAppCopy).ToList();
        if (newLocked != null || missing.Count > 0)
        {
            var save = new ZkAppKeysRequest { AppId = appId, ZkMasterKeyEncryptedAppKey = newLocked };
            foreach (var drive in missing)
            {
                save.ZkAppKeyEncryptedStorageKeys[drive.DriveId] =
                    ZkBrowser.Lock(ZkBrowser.Open(drive.ZkMasterKeyEncryptedStorageKey!, zkMasterKey), appKey);
            }

            var response = await ZkOwnerSteps.Client(owner).SaveAppKeys(save);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        }

        return (appKey, newLocked ?? app.ZkMasterKeyEncryptedAppKey!);
    }

    /// <summary>Authorize again once consent is given, and exchange the code for the token, as the app does.</summary>
    public static async Task<(AppSession Device, YouAuthTokenResponse Token)> CompleteSignInAsync(OwnerSession owner, Guid appId,
        YouAuthFlow.KeyPair keyPair, YouAuthAuthorizeRequest request)
    {
        var response = await YouAuthFlow.AuthorizeAsync(owner.Host, owner, request);
        YouAuthFlow.AssertRedirectsTo(response, request.RedirectUri);
        var (token, exchangeSecret) = await YouAuthFlow.ExchangeTokenAsync(owner.Host, owner.Identity.DomainName, keyPair,
            response.GetHeaderValue("Location")!);

        var (authToken, sharedSecret) = YouAuthFlow.OpenAppToken(token, exchangeSecret);
        return (AppSession.FromToken(owner, appId, authToken, sharedSecret), token);
    }

    /// <summary>The sign-in's private key as PKCS8, which the browser-side helpers open a sealed value with.</summary>
    public static byte[] Pkcs8(YouAuthFlow.KeyPair keyPair)
    {
        using var jwk = JsonDocument.Parse(keyPair.FullKey.PrivateKeyJwk(keyPair.PrivateKey));
        byte[] Member(string name) => Base64UrlEncoder.Decode(jwk.RootElement.GetProperty(name).GetString()!);

        using var ecdh = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP384,
            D = Member("d"),
            Q = new ECPoint { X = Member("x"), Y = Member("y") }
        });
        return ecdh.ExportPkcs8PrivateKey();
    }

    /// <summary>The grant on <paramref name="drive"/> in a caller's security context.</summary>
    public static async Task<RedactedDriveGrant> GrantOnAsync(AppSession app, TargetDrive drive)
    {
        var response = await app.Auth.GetSecurityContext();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return response.Content!.PermissionContext.PermissionGroups.SelectMany(g => g.DriveGrants)
            .Single(g => g.PermissionedDrive.Drive == drive);
    }
}
