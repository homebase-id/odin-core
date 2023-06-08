#nullable enable
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Base;
using Odin.Hosting.Authentication.ClientToken;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.OwnerToken;

namespace Odin.Hosting.Controllers.Anonymous
{
    [ApiController]
    [Route(AppApiPathConstants.BasePathV1)]
    [Route(YouAuthApiPathConstants.BasePathV1)]
    [Route(OwnerApiPathConstants.BasePathV1)]
    public class OptionsController : OdinControllerBase
    {
        [HttpOptions("{**thePath}")]
        public IActionResult Options(string thePath)
        {
            this.Response.Headers.Add("Access-Control-Allow-Origin", (string)this.Request.Headers["Origin"]);
            this.Response.Headers.Add("Access-Control-Allow-Headers",
                new[]
                {
                    "Content-Type", "Accept", ClientTokenConstants.ClientAuthTokenCookieName,
                    OdinHeaderNames.FileSystemTypeHeader
                });
            this.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            this.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
            this.Response.Headers.Add("Access-Control-Expose-Headers", "*");
            this.Response.Headers.Add("Access-Control-Max-Age", "86400");

            return Ok();
        }
    }
}
