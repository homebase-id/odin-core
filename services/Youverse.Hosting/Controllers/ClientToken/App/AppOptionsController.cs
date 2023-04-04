#nullable enable
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Authentication.ClientToken;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.ClientToken.App
{
    [ApiController]
    [Route(AppApiPathConstants.BasePathV1)]
    public class AppOptionsController : OdinControllerBase
    {
        [HttpOptions("{**thePath}")]
        public IActionResult Options(string thePath)
        {
            this.Response.Headers.Add("Access-Control-Allow-Origin", (string)this.Request.Headers["Origin"]);
            this.Response.Headers.Add("Access-Control-Allow-Headers",
                new[]
                {
                    "Content-Type", "Accept", ClientTokenConstants.ClientAuthTokenCookieName,
                    DotYouHeaderNames.FileSystemTypeHeader
                });
            this.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            this.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
            this.Response.Headers.Add("Access-Control-Expose-Headers", "*");

            return Ok();
        }
    }
}