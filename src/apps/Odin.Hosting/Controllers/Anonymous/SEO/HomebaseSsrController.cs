using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Services.LinkPreview;
using Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;
using Odin.Services.LinkPreview.Posts;
using Org.BouncyCastle.Ocsp;

namespace Odin.Hosting.Controllers.Anonymous.SEO;

[Route(LinkPreviewDefaults.SsrPath)]
[ApiController]
public class HomebaseSsrController(
    HomebaseProfileContentService profileContentService,
    HomebaseChannelContentService channelContentService,
    ILogger<HomebaseSsrController> logger) : OdinControllerBase
{
    [HttpGet("")]
    [HttpGet("home")]
    public async Task RenderHome()
    {
        var (head, person) = await BuildHeadSection();

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
        var (head, _) = await BuildHeadSection(suffix: "Connections");
        var body = "";
        await WriteContent(head, body);
    }

    [HttpGet("links")]
    public async Task RenderLinks()
    {
        var (head, _) = await BuildHeadSection(suffix: "Links");
        var body = "";
        await WriteContent(head, body);
    }

    [HttpGet("about")]
    public async Task RenderAbout()
    {
        var (head, _) = await BuildHeadSection(suffix: "About");
        var body = "";
        await WriteContent(head, body);
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
            contentBuilder.AppendLine($"  <h2><a href=\"/posts/{channel.Slug}\">{HttpUtility.HtmlEncode(channel.Name ?? "")}</a></h2>");
            if (!string.IsNullOrWhiteSpace(channel.Description))
            {
                contentBuilder.AppendLine($"  <p>{HttpUtility.HtmlEncode(channel.Description ?? "")}</p>");
            }

            contentBuilder.AppendLine("</div>");
            contentBuilder.AppendLine("<hr/>");
        }

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
        var (posts, cursor) = await channelContentService.GetChannelPosts(channelKey, WebOdinContext);

        var contentBuilder = new StringBuilder();
        foreach (var post in posts)
        {
            var content = post.Content;
            if (content == null) continue;

            contentBuilder.AppendLine("<div>");

            // Title with link
            if (!string.IsNullOrWhiteSpace(content.Caption))
            {
                var link = $"/posts/{channelKey}/{content.Slug}";
                contentBuilder.AppendLine($"  <h3><a href=\"{link}\">{HttpUtility.HtmlEncode(content.Caption)}</a></h3>");
            }

            // Abstract
            if (!string.IsNullOrWhiteSpace(content.Abstract))
            {
                contentBuilder.AppendLine($"  <p>{HttpUtility.HtmlEncode(content.Abstract)}</p>");
            }

            // Image
            if (!string.IsNullOrWhiteSpace(post.ImageUrl))
            {
                contentBuilder.AppendLine(
                    $"  <img src=\"{HttpUtility.HtmlEncode(post.ImageUrl)}\" alt=\"{HttpUtility.HtmlEncode(content.Caption)}\" />");
            }

            contentBuilder.AppendLine("</div>");
            contentBuilder.AppendLine("<hr/>"); // spacing between posts
        }

        await WriteContent(head, contentBuilder.ToString());
    }

    [HttpGet("posts/{channelKey}/{postKey}")]
    public async Task RenderPostDetail(string channelKey, string postKey)
    {
        if (string.IsNullOrWhiteSpace(channelKey) || string.IsNullOrWhiteSpace(postKey))
        {
            throw new OdinClientException("Missing channel or post key");
        }

        var (head, _) = await BuildHeadSection(suffix: "Posts", siteType: "website");
        var post = await channelContentService.GetPost(
            channelKey, postKey, WebOdinContext, HttpContext.RequestAborted);

        if (post == null)
        {
            Response.StatusCode = 404;
            return;
        }

        var contentBuilder = new StringBuilder();
        contentBuilder.AppendLine($"<body>");
        contentBuilder.AppendLine($"<h1>{post.Content.Caption}</h1>");
        contentBuilder.AppendLine($"<img src='{post.ImageUrl}' width='600'/>");
        contentBuilder.AppendLine($"<p>{post.Content.Caption}</p>");
        contentBuilder.AppendLine($"<hr/>");
        try
        {
            var bodyJson = Convert.ToString(post.Content.Body) ?? string.Empty;
            if (!string.IsNullOrEmpty(bodyJson))
            {
                var bodyHtml = PlateRichTextParser.Parse(bodyJson);
                contentBuilder.Append(bodyHtml);
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Failed to Post article body");
        }

        var (otherPosts, _) = await channelContentService.GetChannelPosts(
            channelKey,
            WebOdinContext,
            post.Content.UserDate,
            maxPosts: 10,
            HttpContext.RequestAborted);

        contentBuilder.AppendLine($"<hr/>");
        contentBuilder.AppendLine($"<h3>See More</h1>");

        foreach (var anovahPost in otherPosts)
        {
            var link = $"/posts/{channelKey}/{anovahPost.Content.Slug}";
            contentBuilder.AppendLine($"  <h5><a href=\"{link}\">{HttpUtility.HtmlEncode(anovahPost.Content.Caption)}</a></h5>");
        }

        contentBuilder.AppendLine($"</body>");
        await WriteContent(head, contentBuilder.ToString());
    }

    private async Task<(string head, PersonSchema person)> BuildHeadSection(
        string suffix = LinkPreviewDefaults.DefaultTitle,
        string siteType = "profile")
    {
        var person = await profileContentService.LoadPersonSchema();
        var title = $"{person?.Name ?? WebOdinContext.Tenant} | {suffix}";
        var description = person?.Description ?? LinkPreviewDefaults.DefaultDescription;
        var imageUrl = profileContentService.GetPublicImageUrl(WebOdinContext);

        var head = LayoutBuilder.BuildHeadContent(title, description, siteType, imageUrl, person, HttpContext, WebOdinContext);
        return (head.ToString(), person);
    }

    private async Task WriteContent(string head, string body)
    {
        var html = LayoutBuilder.Wrap(head, body);
        Response.ContentType = "text/html; charset=utf-8";
        await Response.WriteAsync(html);
    }
}