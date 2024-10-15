using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.APIv2.Base;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.APIv2._Internal
{
    /// <summary />
    [ApiController]
    [OdinAuthorizeRoute(RootApiRoutes.Owner | RootApiRoutes.Apps | RootApiRoutes.Guest)]
    public class ApiPolicyTestController : OdinControllerBase
    {
        [HttpPost("")]
        public Task<IActionResult> GetTest()
        {
            return Task.FromResult<IActionResult>(Ok());
        }
    }
}