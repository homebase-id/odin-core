﻿using Microsoft.AspNetCore.Mvc;
using Odin.Services.Optimization.Cdn;
using Odin.Hosting.Controllers.Base.Cdn;

namespace Odin.Hosting.Controllers.ClientToken.App.Cdn
{
    [ApiController]
    [Route(AppApiPathConstants.CdnV1)]
    [AuthorizeValidAppToken]
    public class AppStaticFileContentController : StaticFileContentPublishControllerBase
    {
        public AppStaticFileContentController(StaticFileContentService staticFileContentService) : base(staticFileContentService)
        {
        }
    }
}