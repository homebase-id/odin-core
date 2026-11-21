#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Odin.Services.Authentication.Owner;
using Odin.Services.EncryptionKeyService;

namespace Odin.Hosting.UnifiedV2.SignIn;

/// <summary>
/// Owner login, logout and the password flows under <c>/api/v2/auth/owner</c>: <see cref="OwnerAuthenticationController"/>
/// itself, every action at V1's relative path.
/// </summary>
/// <remarks>
/// No authentication scheme, as on V1: these answer a browser with no session, one with a stale session, and one during
/// a version upgrade (<c>VersionUpgradeMiddleware</c> lets this path through). The unified anonymous policy would read
/// the anonymous drives first. verifyToken and logout read the owner cookie themselves.
/// </remarks>
[Route(UnifiedApiRouteConstants.OwnerAuth)]
[ApiExplorerSettings(GroupName = "v2")]
[NoSharedSecretOnRequest]
[NoSharedSecretOnResponse]
public class V2OwnerAuthenticationController(
    OwnerAuthenticationService authService,
    OwnerSecretService ss,
    PublicPrivateKeyService publicPrivateKeyService,
    ILogger<OwnerAuthenticationController> logger)
    : OwnerAuthenticationController(authService, ss, publicPrivateKeyService, logger);
