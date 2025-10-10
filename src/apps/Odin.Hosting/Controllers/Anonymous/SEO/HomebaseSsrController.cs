using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;
using Odin.Services.LinkPreview;
using Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;
using Odin.Services.LinkPreview.Posts;
using Odin.Services.LinkPreview.Profile;

namespace Odin.Hosting.Controllers.Anonymous.SEO;

[Route(LinkPreviewDefaults.SsrPath)]
[ApiController]
public class HomebaseSsrController(
    HomebaseSsrService ssrService,
    HomebaseProfileContentService profileContentService,
    HomebaseChannelContentService channelContentService) : OdinControllerBase
{
    [HttpGet("")]
    [HttpGet("home")]
    public async Task RenderHome()
    {
        var (head, person) = await BuildHeadSection();

        var contentBuilder = new StringBuilder();
        if (person != null)
        {
            ssrService.WriteHomeBodyContent(contentBuilder, person, WebOdinContext);
        }

        await WriteContent(head, contentBuilder.ToString());
    }

    [HttpGet("connections")]
    public async Task RenderConnections()
    {
        var (head, person) = await BuildHeadSection(suffix: "Connections");
        var contentBuilder = new StringBuilder();
        await ssrService.WriteConnectionsBodyContent(contentBuilder, person, WebOdinContext);
        await WriteContent(head, contentBuilder.ToString());
    }

    [HttpGet("links")]
    public async Task RenderLinks()
    {
        var (head, person) = await BuildHeadSection(suffix: "Links");
        var contentBuilder = new StringBuilder();
        await ssrService.WriteLinksBodyContent(contentBuilder, person);
        CreateMenu(contentBuilder);

        await WriteContent(head, contentBuilder.ToString());
    }

    [HttpGet("about")]
    public async Task RenderAbout()
    {
        var (head, person) = await BuildHeadSection(suffix: "About");

        var contentBuilder = new StringBuilder();
        await ssrService.WriteAboutBodyContent(contentBuilder, person, WebOdinContext);

        CreateMenu(contentBuilder);
        await WriteContent(head, contentBuilder.ToString());
    }

    [HttpGet("posts")]
    public async Task RenderChannelList()
    {
        var (head, _) = await BuildHeadSection(suffix: "Posts", siteType: "website");

        // get a list of channels

        var channels = await channelContentService.GetChannels(WebOdinContext);

        var contentBuilder = new StringBuilder();
        foreach (var channel in channels)
        {
            contentBuilder.AppendLine("<div>");

            // Channel name with link to /posts/{slug}
            contentBuilder.AppendLine($"<h1><a href='{SsrUrlHelper.ToSsrUrl($"/posts/{channel.Slug}")}'>" +
                                      $"{HttpUtility.HtmlEncode(channel.Name ?? "")}</a></h1>");

            if (!string.IsNullOrWhiteSpace(channel.Description))
            {
                contentBuilder.AppendLine($"  <p>{HttpUtility.HtmlEncode(channel.Description ?? "")}</p>");
            }

            contentBuilder.AppendLine("</div>");
            contentBuilder.AppendLine("<hr/>");
        }

        CreateMenu(contentBuilder);
        await WriteContent(head, contentBuilder.ToString());
    }

    [HttpGet("posts/{channelKey}")]
    public async Task RenderPostsOnChannel(string channelKey)
    {
        if (string.IsNullOrWhiteSpace(channelKey))
        {
            throw new OdinClientException("Missing channel key");
        }

        var (head, _) = await BuildHeadSection(suffix: "Posts", siteType: "website");
        var (posts, _) = await channelContentService.GetChannelPosts(channelKey, WebOdinContext);

        var thisChannel = (await channelContentService.GetChannels(WebOdinContext)).FirstOrDefault(c => c.Slug == channelKey);

        var contentBuilder = new StringBuilder();
        ssrService.WriteChannelPostListBody(channelKey, thisChannel, contentBuilder, posts);

        await WriteContent(head, contentBuilder.ToString());
    }
    
    [HttpGet("posts/{channelKey}/{postKey}")]
    public async Task RenderPostDetail(string channelKey, string postKey)
    {
        if (string.IsNullOrWhiteSpace(channelKey) || string.IsNullOrWhiteSpace(postKey))
        {
            throw new OdinClientException("Missing channel or post key");
        }

        var post = await channelContentService.GetPost(
            channelKey, postKey, WebOdinContext, HttpContext.RequestAborted);

        if (post == null)
        {
            Response.StatusCode = 404;
            return;
        }

        // We need the post title in the tab title xxx
        string suffix = string.IsNullOrEmpty(post.Content.Caption) ? "Posts" : post.Content.Caption;
        var (head, _) = await BuildHeadSection(suffix: suffix, siteType: "website");

        var contentBuilder = new StringBuilder();
        await ssrService.WritePostBodyContent(channelKey, post, contentBuilder, WebOdinContext, HttpContext.RequestAborted);
        await WriteContent(head, contentBuilder.ToString());
    }

    private async Task<(string head, PersonSchema person)> BuildHeadSection(
        string suffix = LinkPreviewDefaults.DefaultTitle,
        string siteType = "profile")
    {
        var person = await profileContentService.LoadPersonSchema();
        var title = $"{person?.Name ?? WebOdinContext.Tenant} | {suffix}";
        var description = DataOrNull(person?.BioSummary) ??
                          Truncate(DataOrNull(person?.Bio), 160) ?? LinkPreviewDefaults.DefaultDescription;
        var imageUrl = profileContentService.GetPublicImageUrl(WebOdinContext);

        var head = LayoutBuilder.BuildHeadContent(title, description, siteType, imageUrl, person, HttpContext, WebOdinContext);
        return (head.ToString(), person);
    }

    private static void CreateMenu(StringBuilder contentBuilder)
    {
        contentBuilder.AppendLine($"<ul>");
        contentBuilder.AppendLine($"<li><a href='{SsrUrlHelper.ToSsrUrl("posts")}'>See my Posts</a></li>");
        contentBuilder.AppendLine($"<li><a href='{SsrUrlHelper.ToSsrUrl("connections")}'>See my connections</a></li>");
        contentBuilder.AppendLine($"<li><a href='{SsrUrlHelper.ToSsrUrl("about")}'>About me</a></li>");
        contentBuilder.AppendLine($"<li><a href='{SsrUrlHelper.ToSsrUrl("links")}'>See my links</a></li>");
        contentBuilder.AppendLine($"</ul>");
    }

    private string DataOrNull(string data)
    {
        return string.IsNullOrEmpty(data) ? null : data;
    }

    private string Truncate(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input) || maxLength <= 0)
        {
            return input;
        }

        if (input.Length <= maxLength)
        {
            return input;
        }

        return input.Substring(0, maxLength);
    }

    private async Task WriteContent(string head, string body)
    {
        var html = LayoutBuilder.Wrap(head, body);
        Response.ContentType = "text/html; charset=utf-8";
        await Response.WriteAsync(html);
    }
}