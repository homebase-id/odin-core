#nullable enable
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;

namespace Odin.Hosting.Controllers.Anonymous
{
    [ApiController]
    [Route(AppApiPathConstantsV1.BasePathV1)]
    [Route(AppApiPathConstantsV2.BasePathV2)]
    [Route(GuestApiPathConstantsV1.BasePathV1)]
    [Route(GuestApiPathConstantsV2.BasePathV2)]
    [Route(OwnerApiPathConstants.BasePathV1)]
    public class OptionsController : OdinControllerBase
    {
        [HttpOptions("{**thePath}")]
        public IActionResult Options(string thePath)
        {
            Response.Headers.Append("Access-Control-Allow-Origin", Request.Headers["Origin"]);
            Response.Headers.Append("Access-Control-Allow-Headers",
                new[]
                {
                    "Content-Type", "Accept", YouAuthConstants.AppCookieName, YouAuthConstants.SubscriberCookieName,
                    OdinHeaderNames.FileSystemTypeHeader
                });
            Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS, PATCH, PUT, DELETE");
            Response.Headers.Append("Access-Control-Allow-Credentials", "true");
            Response.Headers.Append("Access-Control-Expose-Headers", "*");
            Response.Headers.Append("Access-Control-Max-Age", "86400");

            return Ok();
        }
    }
}
