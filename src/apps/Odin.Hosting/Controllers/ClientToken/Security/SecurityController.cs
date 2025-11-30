using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Hosting.Controllers.Anonymous;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.ClientToken.Shared;

namespace Odin.Hosting.Controllers.ClientToken.Security;

/// <summary>
/// Security information for the current user
/// </summary>
[ApiController]
[Route(AppApiPathConstantsV1.SecurityV1)]
[Route(GuestApiPathConstantsV1.SecurityV1)]
[AuthorizeValidGuestOrAppToken]
public class SecurityController : OdinControllerBase
{

    /// <summary />
    public SecurityController()
    {
    }

    /// <summary>
    /// Returns redacted security information for the currently logged in user
    /// </summary>
    /// <returns></returns>
    [HttpGet("context")]
    public RedactedOdinContext GetSecurityContext()
    {
        return WebOdinContext.Redacted();
    }
}