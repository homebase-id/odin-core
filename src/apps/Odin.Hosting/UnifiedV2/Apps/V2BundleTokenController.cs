#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authorization.BundleTokens;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Apps;

/// <summary>
/// Bundle tokens: one client token mapped to several registered apps.
/// </summary>
/// <remarks>
/// Issuance mirrors YouAuth's two steps.  The owner console posts the client's public key to
/// <c>POST /</c> and hands the returned exchange key and salt back to the client, which derives the same
/// ECDH secret and collects its sealed token, once, from the anonymous <c>POST /exchange</c>.
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.BundleTokens)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2BundleTokenController(BundleTokenService bundleTokenService) : OdinControllerBase
{
    /// <summary>Issues a bundle token for the client whose public key is in the request.</summary>
    [HttpPost]
    [UnifiedV2Authorize(UnifiedPolicies.Owner)]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<BeginBundleTokenExchangeResponse> Issue([FromBody] BeginBundleTokenExchangeRequest request)
    {
        return await bundleTokenService.BeginExchangeAsync(request, WebOdinContext);
    }

    /// <summary>
    /// The client collects its sealed token with the SHA-256 digest of the ECDH secret.  Same response as
    /// YouAuth's <c>token</c> endpoint.  404 when the digest is unknown, expired or already collected.
    /// </summary>
    /// <remarks>
    /// Anonymous, like YouAuth's <c>token</c>: the client has no credential yet, and only a holder of the
    /// client's private key can compute the digest or open what is returned.
    /// </remarks>
    [HttpPost("exchange")]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [NoSharedSecretOnRequest]
    [NoSharedSecretOnResponse]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<ActionResult<YouAuthTokenResponse>> Exchange([FromBody] YouAuthTokenRequest request)
    {
        var sealedToken = await bundleTokenService.ExchangeAsync(request?.SecretDigest ?? "");
        if (sealedToken == null)
        {
            return NotFound();
        }

        return new YouAuthTokenResponse
        {
            Base64SharedSecretCipher = Convert.ToBase64String(sealedToken.SharedSecretCipher),
            Base64SharedSecretIv = Convert.ToBase64String(sealedToken.SharedSecretIv),
            Base64ClientAuthTokenCipher = Convert.ToBase64String(sealedToken.ClientAuthTokenCipher),
            Base64ClientAuthTokenIv = Convert.ToBase64String(sealedToken.ClientAuthTokenIv),
        };
    }

    /// <summary>Bundle tokens, optionally only those that reach <paramref name="appId"/>.</summary>
    [HttpGet]
    [UnifiedV2Authorize(UnifiedPolicies.Owner)]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<List<RedactedBundleToken>> GetTokens([FromQuery] Guid? appId)
    {
        return await bundleTokenService.GetTokensAsync(appId, WebOdinContext);
    }

    [HttpPost("{tokenId:guid}/revoke")]
    [UnifiedV2Authorize(UnifiedPolicies.Owner)]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<IActionResult> Revoke([FromRoute] Guid tokenId)
    {
        await bundleTokenService.SetRevokedAsync(tokenId, true, WebOdinContext);
        return NoContent();
    }

    [HttpPost("{tokenId:guid}/allow")]
    [UnifiedV2Authorize(UnifiedPolicies.Owner)]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<IActionResult> Allow([FromRoute] Guid tokenId)
    {
        await bundleTokenService.SetRevokedAsync(tokenId, false, WebOdinContext);
        return NoContent();
    }

    [HttpDelete("{tokenId:guid}")]
    [UnifiedV2Authorize(UnifiedPolicies.Owner)]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<IActionResult> Delete([FromRoute] Guid tokenId)
    {
        await bundleTokenService.DeleteAsync(tokenId, WebOdinContext);
        return NoContent();
    }

    [HttpDelete("{tokenId:guid}/apps/{appId:guid}")]
    [UnifiedV2Authorize(UnifiedPolicies.Owner)]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<IActionResult> RemoveApp([FromRoute] Guid tokenId, [FromRoute] Guid appId)
    {
        await bundleTokenService.RemoveAppAsync(tokenId, appId, WebOdinContext);
        return NoContent();
    }

    /// <summary>Logout: the bundle token making this request deletes itself.</summary>
    [HttpDelete("current")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<IActionResult> DeleteCurrent()
    {
        await bundleTokenService.DeleteCurrentAsync(WebOdinContext);
        return NoContent();
    }
}
