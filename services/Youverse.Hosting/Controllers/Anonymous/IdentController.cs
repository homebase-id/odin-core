#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Base;

namespace Youverse.Hosting.Controllers.Anonymous
{
    [ApiController]
    [Route(YouAuthApiPathConstants.AuthV1)]
    public class IdentController : Controller
    {
        private readonly DotYouContextAccessor _dotYouContextAccessor;

        public IdentController(DotYouContextAccessor dotYouContextAccessor)
        {
            _dotYouContextAccessor = dotYouContextAccessor;
        }

        /// <summary>
        /// Identifies this server as an ODIN identity server
        /// </summary>
        [HttpGet("ident")]
        [Produces("application/json")]
        public async Task<IActionResult> GetInfo()
        {
            return await Task.FromResult(new JsonResult(new
            {
                OdinId = _dotYouContextAccessor.GetCurrent().Tenant
            }));
        }
    }
}