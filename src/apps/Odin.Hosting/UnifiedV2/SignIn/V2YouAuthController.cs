#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Tenant;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.SignIn;

/// <summary>
/// The YouAuth endpoints under <c>/api/v2</c>: <see cref="YouAuthUnifiedController"/> itself, so every step behaves as
/// it does on V1, and every URL it builds from the request (login, app registration, consent) points back here.
/// </summary>
/// <remarks>
/// Keeps V1's owner scheme (<c>AuthorizeValidOwnerToken</c>, inherited) rather than the unified one, because a browser
/// is navigated here, and the unified scheme answers a browser with no owner cookie with a 403: no token is an anonymous
/// success there, so the policy forbids rather than challenges, and nobody is sent to login. The owner scheme also
/// schedules a pending version upgrade, which the consent page depends on.
/// No <c>[ApiController]</c>, as on V1: a request that fails validation is a redirect or a 400 this code chooses.
/// </remarks>
[Route(UnifiedApiRouteConstants.YouAuth)]
[ApiExplorerSettings(GroupName = "v2")]
[NoSharedSecretOnRequest]
[NoSharedSecretOnResponse]
public class V2YouAuthController(
    ILogger<YouAuthUnifiedController> logger,
    ITenantProvider tenantProvider,
    IYouAuthUnifiedService youAuthService,
    YouAuthClientMetadataService clientMetadata)
    : YouAuthUnifiedController(logger, tenantProvider, youAuthService, clientMetadata)
{
    [HttpGet("authorize")]
    [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
    public override Task<ActionResult> Authorize([FromQuery] YouAuthAuthorizeRequest authorize) => base.Authorize(authorize);

    [HttpPost("authorize")]
    [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
    public override Task<ActionResult> Consent(
        [FromForm(Name = YouAuthAuthorizeConsentGiven.ReturnUrlName)]
        string returnUrl,
        [FromForm(Name = YouAuthAuthorizeConsentGiven.ConsentRequirementName)]
        string consentRequirementJson,
        [FromForm(Name = YouAuthAuthorizeConsentGiven.SealedZkAppKeyName)]
        string? sealedZkAppKey = null,
        [FromForm(Name = YouAuthAuthorizeConsentGiven.LockedZkAppKeyName)]
        string? lockedZkAppKey = null) =>
        base.Consent(returnUrl, consentRequirementJson, sealedZkAppKey, lockedZkAppKey);

    [AllowAnonymous]
    [HttpPost("token")]
    [Produces("application/json")]
    [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
    public override Task<ActionResult<YouAuthTokenResponse>> Token([FromBody] YouAuthTokenRequest tokenRequest) => base.Token(tokenRequest);
}
