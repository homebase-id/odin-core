using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.ApiExceptions.Client;
using Odin.Services.EncryptionKeyService;

namespace Odin.Hosting.UnifiedV2.PublicKeys;

/// <summary>
/// The V2 twin of the two of V1's <c>public/keys</c> routes the browser asks for. Public keys, for anyone.
/// </summary>
/// <remarks>
/// No caller is resolved, as on V1, so this skips the unified policies on purpose. These are asked for by a client
/// holding no shared secret, from a browser that may well hold the owner's cookie: resolving it would encrypt the
/// answer with a secret the client hasn't got, and a stale one would turn even the anonymous policy into a 401.
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.PublicKeys)]
[AllowAnonymous]
[NoSharedSecretOnRequest]
[NoSharedSecretOnResponse]
[ApiExplorerSettings(GroupName = "v2")]
public class V2PublicKeyController(PublicPrivateKeyService publicKeyService) : ControllerBase
{
    [HttpGet("offline-ecc")]
    public async Task<string> GetOfflineEccPublicKey()
    {
        var key = await publicKeyService.GetOfflineEccPublicKeyAsync();

        if (key == null)
        {
            throw new NotFoundException("Offline ECC public key not found.");
        }

        var expiration = Math.Min(key.expiration.seconds, 3600);
        Response.Headers.CacheControl = $"public,max-age={expiration}";
        return key.PublicKeyJwkBase64Url();
    }

    [HttpGet("notifications-pk")]
    public async Task<string> GetNotificationsPk()
    {
        return await publicKeyService.GetNotificationsEccPublicKeyAsync();
    }
}
