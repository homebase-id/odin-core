#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Base;
using Odin.Core.Services.Registry;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.ClientToken.Guest
{
    [ApiController]
    [Route(GuestApiPathConstants.AuthV1)]
    public class IdentController : Controller
    {
        private readonly OdinContextAccessor _odinContextAccessor;
        private readonly IIdentityRegistry _registry;


        public IdentController(OdinContextAccessor odinContextAccessor, IIdentityRegistry registry)
        {
            _odinContextAccessor = odinContextAccessor;
            _registry = registry;
        }

        /// <summary>
        /// Identifies this server as an ODIN identity server
        /// </summary>
        [HttpGet("ident")]
        [Produces("application/json")]
        public async Task<IActionResult> GetInfo()
        {
            var tenant = _odinContextAccessor.GetCurrent().Tenant;
            HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", "*");

            if (string.IsNullOrEmpty(tenant))
            {
                return await Task.FromResult(new JsonResult(new GetIdentResponse()
                {
                    OdinId = string.Empty,
                    Version = 1.0
                }));
            }
            
            if(await _registry.IsIdentityRegistered(tenant))
            {
                return await Task.FromResult(new JsonResult(new GetIdentResponse()
                {
                    OdinId = tenant,
                    Version = 1.0
                }));
            }
            
            return await Task.FromResult(new JsonResult(new GetIdentResponse
            {
                OdinId = string.Empty,
                Version = 1.0
            }));
        }
    }
}