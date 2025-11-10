using Microsoft.AspNetCore.Mvc;
using Odin.Services.Optimization.Cdn;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Cdn
{
    [ApiController]
    [Route(AppApiPathConstantsV1.CdnV1)]
    [AuthorizeValidAppToken]
    public class AppStaticFileContentController : StaticFileContentPublishControllerBase
    {
        public AppStaticFileContentController(StaticFileContentService staticFileContentService) : base(staticFileContentService)
        {
        }
    }
}