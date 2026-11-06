#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Home.Auth;
using Odin.Hosting.Controllers.Home.Service;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Tenant;

namespace Odin.Hosting.UnifiedV2.SignIn;

/// <summary>
/// Sign-in on the home site under <c>/api/v2/home/auth</c>: <see cref="HomeAuthenticationController"/> itself, every
/// action at V1's relative path, each with V1's own authentication (the guest scheme for is-authenticated and ping, none
/// for the rest).
/// </summary>
/// <remarks>
/// The guest scheme because is-authenticated asks about the visitor's XT32 cookie. The unified scheme takes the owner's
/// cookie first, so an owner would read as signed in on a page where they are not, and a stale owner cookie would make it
/// a 401. The token exchange at the callback stays on the peer's V1 route: that peer can be running any version.
/// </remarks>
[Route(UnifiedApiRouteConstants.HomeAuth)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2HomeAuthenticationController(
    ILogger<HomeAuthenticationController> logger,
    ITenantProvider tenantProvider,
    HomeAuthenticatorService homeAuthenticatorService,
    PublicPrivateKeyService pkService,
    IOdinHttpClientFactory odinHttpClientFactory)
    : HomeAuthenticationController(logger, tenantProvider, homeAuthenticatorService, pkService, odinHttpClientFactory);
