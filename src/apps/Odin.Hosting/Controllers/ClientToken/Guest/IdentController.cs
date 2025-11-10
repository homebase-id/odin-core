#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Registry;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.ClientToken.Guest
{
    [ApiController]
    [Route(GuestApiPathConstantsV1.AuthV1)]
    public class IdentController : OdinControllerBase
    {
        private readonly IIdentityRegistry _registry;


        public IdentController(IIdentityRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Identifies this server as an ODIN identity server
        /// </summary>
        [HttpGet("ident")]
        [Produces("application/json")]
        public async Task<IActionResult> GetInfo()
        {
            var tenant = WebOdinContext.Tenant;
            HttpContext.Response.Headers.Append("Access-Control-Allow-Origin", "*");

            if (string.IsNullOrEmpty(tenant))
            {
                return new JsonResult(new GetIdentResponse
                {
                    OdinId = string.Empty,
                    Version = 1.0
                });
            }
            
            if(await _registry.IsIdentityRegistered(tenant))
            {
                return new JsonResult(new GetIdentResponse
                {
                    OdinId = tenant,
                    Version = 1.0
                });
            }
            
            return new JsonResult(new GetIdentResponse
            {
                OdinId = string.Empty,
                Version = 1.0
            });
        }
    }
}