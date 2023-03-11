using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Controllers.Anonymous;

namespace Youverse.Hosting.Controllers.ClientToken.Security;

/// <summary>
/// Security information for the current user
/// </summary>
[ApiController]
[Route(AppApiPathConstants.SecurityV1)]
[Route(YouAuthApiPathConstants.SecurityV1)]
[AuthorizeValidExchangeGrant]
public class SecurityController : Controller
{
    private readonly DotYouContextAccessor _contextAccessor;

    /// <summary />
    public SecurityController(DotYouContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    /// <summary>
    /// Returns redacted security information for the currently logged in user
    /// </summary>
    /// <returns></returns>
    [HttpPost("context")]
    public IActionResult GetSecurityContext()
    {
        return new JsonResult(_contextAccessor.GetCurrent().Redacted());
    }
}