#nullable enable
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.OwnerToken;

namespace Odin.Hosting.Controllers.Anonymous
{
    [ApiController]
    [Route(AppApiPathConstants.BasePathV1)]
    [Route(GuestApiPathConstants.BasePathV1)]
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
                    "Content-Type", "Accept", YouAuthConstants.AppCookieName,
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
