#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Registry;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.SignIn;

/// <summary>
/// "Is this host an identity?" under <c>/api/v2/auth/ident</c>: <see cref="IdentController"/> itself, with no
/// authentication, and answering during a version upgrade.
/// </summary>
[Route(UnifiedApiRouteConstants.Auth)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2IdentController(IIdentityRegistry registry) : IdentController(registry)
{
    [HttpGet("ident")]
    [Produces("application/json")]
    [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
    public override Task<IActionResult> GetInfo() => base.GetInfo();
}
