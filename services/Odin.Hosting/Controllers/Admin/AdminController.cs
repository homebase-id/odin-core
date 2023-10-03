using Microsoft.AspNetCore.Mvc;

namespace Odin.Hosting.Controllers.Admin;

[ApiController]
[Route(AdminApiPathConstants.BasePathV1)]
[ServiceFilter(typeof(LocalhostWithApiKeyAttribute))]
public class AdminController : ControllerBase
{
    [HttpGet("ping")]
    public ActionResult<string> Ping()
    {
        return "pong";
    }
}
