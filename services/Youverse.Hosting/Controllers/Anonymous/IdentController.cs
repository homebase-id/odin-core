#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Registry;

namespace Youverse.Hosting.Controllers.Anonymous
{
    [ApiController]
    [Route(YouAuthApiPathConstants.AuthV1)]
    public class IdentController : Controller
    {
        private readonly DotYouContextAccessor _dotYouContextAccessor;
        private readonly IIdentityRegistry _registry;


        public IdentController(DotYouContextAccessor dotYouContextAccessor, IIdentityRegistry registry)
        {
            _dotYouContextAccessor = dotYouContextAccessor;
            _registry = registry;
        }

        /// <summary>
        /// Identifies this server as an ODIN identity server
        /// </summary>
        [HttpGet("ident")]
        [Produces("application/json")]
        public async Task<IActionResult> GetInfo()
        {
            var tenant = _dotYouContextAccessor.GetCurrent().Tenant;
            if(await _registry.IsIdentityRegistered(tenant))
            {
                return await Task.FromResult(new JsonResult(new
                {
                    OdinId = tenant
                }));
            }
            
            return await Task.FromResult(new JsonResult(new
            {
                OdinId = string.Empty
            }));
        }
    }
}