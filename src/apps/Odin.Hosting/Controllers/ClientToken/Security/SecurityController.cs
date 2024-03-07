using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Base;
using Odin.Hosting.Controllers.Anonymous;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.ClientToken.Shared;

namespace Odin.Hosting.Controllers.ClientToken.Security;

/// <summary>
/// Security information for the current user
/// </summary>
[ApiController]
[Route(AppApiPathConstants.SecurityV1)]
[Route(GuestApiPathConstants.SecurityV1)]
[AuthorizeValidGuestOrAppToken]
public class SecurityController : Controller
{
    private readonly OdinContextAccessor _contextAccessor;

    /// <summary />
    public SecurityController(OdinContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    /// <summary>
    /// Returns redacted security information for the currently logged in user
    /// </summary>
    /// <returns></returns>
    [HttpGet("context")]
    public RedactedOdinContext GetSecurityContext()
    {
        return _contextAccessor.GetCurrent().Redacted();
    }
}