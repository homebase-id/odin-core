using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Controllers.OwnerToken.Security;

/// <summary>
/// Security information for the current user
/// </summary>
[ApiController]
[Route(OwnerApiPathConstants.SecurityV1)]
[AuthorizeValidOwnerToken]
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