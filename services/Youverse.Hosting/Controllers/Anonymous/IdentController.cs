#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Youverse.Hosting.Controllers.Anonymous
{
    [ApiController]
    [Route(YouAuthApiPathConstants.AuthV1)]
    public class IdentController : Controller
    {
        /// <summary>
        /// Identifies this server as an ODIN identity server
        /// </summary>
        [HttpGet("ident")]
        [Produces("application/json")]
        public async Task<IActionResult> GetInfo()
        {
            return await Task.FromResult(new JsonResult(new
            {
                id = 42
            }));
        }
    }
}