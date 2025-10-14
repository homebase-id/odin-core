using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.LinkPreview;

namespace Odin.Hosting.Controllers.Anonymous.SEO;

[Route("")]
[ApiController]
public class HomebaseSitemapController(HomebaseSsrService ssrService) : OdinControllerBase
{
    [HttpGet("sitemap.xml")]
    public async Task RenderSiteMap()
    {
        var contentBuilder = new StringBuilder();
        await ssrService.WriteSitemap(contentBuilder, WebOdinContext);
        Response.ContentType = "text/xml; charset=utf-8";

        var seconds = TimeSpan.FromDays(7).TotalSeconds;
        Response.Headers.TryAdd("Cache-Control", $"max-age={seconds}");

        await Response.WriteAsync(contentBuilder.ToString());
    }
}