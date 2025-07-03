using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Services.LinkPreview;
using Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;

namespace Odin.Hosting.Controllers.Anonymous.SEO;

[Route(LinkPreviewDefaults.SsrPath)]
[ApiController]
public class HomebaseSsrController(HomebaseProfileContentLoader profileContentLoader) : OdinControllerBase
{

    [HttpGet("")]
    [HttpGet("home")]
    public async Task RenderHome()
    {
        var (head, person) = await BuildDefaultHead();

        var body = new StringBuilder();
        if (person != null)
        {
            if (person.BioSummary != null)
            {
                body.AppendLine($"<h2>Summary: {person.BioSummary}</h2>");
            }
            
            if (person.Bio != null)
            {
                body.AppendLine($"<hr/>");
                body.AppendLine($"<p>Bio: {person.Bio}</p>");
            }
        }

        await WriteContent(head, body.ToString());
    }

    [HttpGet("connections")]
    public async Task RenderConnections()
    {
        var (head, _) = await BuildDefaultHead(suffix: "Connections");
        var body = "";
        await WriteContent(head, body);
    }

    [HttpGet("links")]
    public async Task RenderLinks()
    {
        var (head, _) = await BuildDefaultHead(suffix: "Links");
        var body = "";
        await WriteContent(head, body);
    }

    [HttpGet("about")]
    public async Task RenderAbout()
    {
        var (head, _) = await BuildDefaultHead(suffix: "About");
        var body = "";
        await WriteContent(head, body);
    }

    [HttpGet("posts")]
    public async Task RenderPostList()
    {
        var (head, _) = await BuildDefaultHead(suffix: "Posts", siteType: "website");
        var body = "TODO: render list of posts across channels";
        await WriteContent(head, body);
    }

    [HttpGet("posts/{channelKey}")]
    public async Task RenderPostChannel(string channelKey)
    {
        if (string.IsNullOrWhiteSpace(channelKey))
        {
            throw new OdinClientException("Missing channel key");
        }

        var (head, _) = await BuildDefaultHead(suffix: "Posts", siteType: "website");
        var body = "TODO: render list of posts in channel";
        await WriteContent(head, body);
    }

    [HttpGet("posts/{channelKey}/{postKey}")]
    public async Task RenderPostDetail(string channelKey, string postKey)
    {
        if (string.IsNullOrWhiteSpace(channelKey) || string.IsNullOrWhiteSpace(postKey))
        {
            throw new OdinClientException("Missing channel or post key");
        }

        var (head, _) = await BuildDefaultHead(suffix: "Posts", siteType: "website");
        var body = "TODO: render list of posts detail";
        await WriteContent(head, body);
    }

    private async Task<(string head, PersonSchema person)> BuildDefaultHead(
        string suffix = LinkPreviewDefaults.DefaultTitle,
        string siteType = "profile")
    {
        var person = await profileContentLoader.LoadPersonSchema();
        var title = $"{person?.Name ?? WebOdinContext.Tenant} | {suffix}";
        var description = person?.Description ?? LinkPreviewDefaults.DefaultDescription;
        var imageUrl = profileContentLoader.GetPublicImageUrl(WebOdinContext);

        var head = LayoutBuilder.BuildHead(title, description, siteType, imageUrl, person, HttpContext, WebOdinContext);
        return (head.ToString(), person);
    }


    private async Task WriteContent(string head, string body)
    {
        var html = LayoutBuilder.Wrap(head, body.ToString());
        Response.ContentType = "text/html; charset=utf-8";
        await Response.WriteAsync(html);
    }
}