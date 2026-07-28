using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;
using Odin.Services.PublicPage;

namespace Odin.Hosting.Controllers.Anonymous.SEO;

[Route("")]
[ApiController]
public class HomebaseSitemapController(HomebaseSsrService ssrService, TenantContext tenantContext) : OdinControllerBase
{
    [HttpGet("sitemap.xml")]
    public async Task RenderSiteMap()
    {
        if (!tenantContext.EnablePublicWebPresence)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var contentBuilder = new StringBuilder();
        await ssrService.WriteSitemap(contentBuilder, WebOdinContext);
        Response.ContentType = "text/xml; charset=utf-8";

        var seconds = TimeSpan.FromDays(7).TotalSeconds;
        Response.Headers.TryAdd("Cache-Control", $"max-age={seconds}");

        await Response.WriteAsync(contentBuilder.ToString());
    }

    [HttpGet("robots.txt")]
    public IActionResult RenderRobots()
    {
        if (!tenantContext.EnablePublicWebPresence)
        {
            return Content("""
                           User-agent: *
                           Disallow: /
                           """, "text/plain");
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var content = $"""
                       User-agent: *
                       Disallow: /preview

                       Sitemap: {baseUrl}/sitemap.xml
                       """;

        return Content(content, "text/plain");
    }
}