using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authorization.Permissions;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.LinkPreview;
using Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;
using Odin.Services.LinkPreview.Posts;
using Odin.Services.LinkPreview.Profile;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Controllers.Anonymous.SEO;

[Route(LinkPreviewDefaults.SsrPath)]
[ApiController]
public class HomebaseSsrController(
    HomebaseProfileContentService profileContentService,
    HomebaseChannelContentService channelContentService,
    CircleNetworkService cn,
    FollowerService followerService,
    ILogger<HomebaseSsrController> logger) : OdinControllerBase
{
    [HttpGet("")]
    [HttpGet("home")]
    public async Task RenderHome()
    {
        var (head, person) = await BuildHeadSection();

        var contentBuilder = new StringBuilder();
        if (person != null)
        {
            var imageUrl = profileContentService.GetPublicImageUrl(WebOdinContext);
            contentBuilder.AppendLine($"<img src='{imageUrl}' width='600'/>");
            contentBuilder.AppendLine($"<h1>{person.Name}</h1>");
            contentBuilder.AppendLine($"<h2>{person.Status}</h2>");

            if (person.BioSummary != null)
            {
                contentBuilder.AppendLine($"<h3>Summary: {person.BioSummary}</h3>");
            }

            if (person.Bio != null)
            {
                contentBuilder.AppendLine($"<hr/>");
                contentBuilder.AppendLine($"<p>Bio: {person.Bio}</p>");
            }

            CreateMenu(contentBuilder);
        }

        await WriteContent(head, contentBuilder.ToString());
    }

    [HttpGet("connections")]
    public async Task RenderConnections()
    {
        var (head, person) = await BuildHeadSection(suffix: "Connections");

        var contentBuilder = new StringBuilder();
        contentBuilder.AppendLine($"<h1>{person.Name}</h1>");
        contentBuilder.AppendLine($"<h2>{person.Status}</h2>");

        if (person.BioSummary != null)
        {
            contentBuilder.AppendLine($"<h3>Summary: {person.BioSummary}</h3>");
        }

        var count = Int32.MaxValue;
        if (WebOdinContext.PermissionsContext?.HasPermission(PermissionKeys.ReadConnections) ?? false)
        {
            var result = await cn.GetConnectedIdentitiesAsync(count, null, WebOdinContext);
            var connections = result.Results.Select(p => p.Redacted()).ToList();

            contentBuilder.AppendLine("<h3>My Connections</h3>");
            contentBuilder.AppendLine("<ul>");

            foreach (var identity in connections)
            {
                var odinId = identity.OdinId;
                var imageUrl = $"https://{odinId}/pub/image";
                contentBuilder.AppendLine("<li>");
                contentBuilder.AppendLine($"<img src=\"{imageUrl}\" alt=\"Status\" width=\"24\" height=\"24\"" +
                                          $" style=\"vertical-align: middle; margin-right: 8px;\"/>");
                contentBuilder.AppendLine($"<a href='https://{odinId}/ssr'>{WebUtility.HtmlEncode(odinId)}</a>");

                contentBuilder.AppendLine("  </li>");
            }

            contentBuilder.AppendLine("</ul>");
        }

        if (WebOdinContext.PermissionsContext?.HasPermission(PermissionKeys.ReadWhoIFollow) ?? false)
        {
            var peopleIFollow = await followerService.GetIdentitiesIFollowAsync(count, null, WebOdinContext);
            contentBuilder.AppendLine("<h3>Who I follow</h3>");
            contentBuilder.AppendLine("<ul>");

            foreach (var identity in peopleIFollow.Results)
            {
                var odinId = identity;
                var imageUrl = $"https://{odinId}/pub/image";
                contentBuilder.AppendLine("  <li>");

                contentBuilder.AppendLine($"<img src=\"{imageUrl}\" alt=\"Status\" width=\"24\" " +
                                          $"height=\"24\" style=\"vertical-align: middle; margin-right: 8px;\"/>");
                contentBuilder.AppendLine($"<a href='https://{odinId}/ssr'>{WebUtility.HtmlEncode(odinId)}</a>");
                contentBuilder.AppendLine("  </li>");
            }

            contentBuilder.AppendLine("</ul>");
        }

        CreateMenu(contentBuilder);

        await WriteContent(head, contentBuilder.ToString());
    }

    [HttpGet("links")]
    public async Task RenderLinks()
    {
        var (head, person) = await BuildHeadSection(suffix: "Links");
        var links = await profileContentService.LoadLinks();
        var contentBuilder = new StringBuilder();

        contentBuilder.AppendLine($"<h1>{person.Name}</h1>");
        contentBuilder.AppendLine($"<h2>{person.Status}</h2>");

        if (person.BioSummary != null)
        {
            contentBuilder.AppendLine($"<h3>Summary: {person.BioSummary}</h3>");
        }

        contentBuilder.AppendLine("<h3>My Links</h3>");
        contentBuilder.AppendLine("<ul>");
        foreach (var link in links)
        {
            contentBuilder.AppendLine("  <li>");
            contentBuilder.AppendLine($"    <a href='{link.Url}'>{WebUtility.HtmlEncode(link.Type)}</span>");
            contentBuilder.AppendLine("  </li>");
        }

        contentBuilder.AppendLine("</ul>");

        CreateMenu(contentBuilder);

        await WriteContent(head, contentBuilder.ToString());
    }

    [HttpGet("about")]
    public async Task RenderAbout()
    {
        var (head, person) = await BuildHeadSection(suffix: "About");

        var contentBuilder = new StringBuilder();
        contentBuilder.AppendLine($"<h1>{person.Name}</h1>");
        contentBuilder.AppendLine($"<h2>{person.Status}</h2>");

        var aboutSection = await profileContentService.LoadAboutSection(WebOdinContext);
        if (null == aboutSection)
        {
            CreateMenu(contentBuilder);
            await WriteContent(head, contentBuilder.ToString());
            return;
        }

        contentBuilder.AppendLine("<br/><hr/><br/>");

        contentBuilder.AppendLine("<h2>Status</h2>");
        foreach (var status in aboutSection.Status)
        {
            contentBuilder.AppendLine($"<p>{status}</p>");
        }

        contentBuilder.AppendLine("<br/><hr/><br/>");

        contentBuilder.AppendLine("<h2>Bio</h2>");
        foreach (var bio in aboutSection.Bio)
        {
            try
            {
                if (!string.IsNullOrEmpty(bio))
                {
                    var bodyHtml = PlateRichTextParser.Parse(bio);
                    contentBuilder.AppendLine($"<div>");
                    contentBuilder.Append(bodyHtml);
                    contentBuilder.AppendLine($"</div>");
                }
            }
            catch (JsonException) // this fallback is for older bios that have not been changed to json format.
            {
                contentBuilder.AppendLine($"<div>");
                contentBuilder.Append(bio);
                contentBuilder.AppendLine($"</div>");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not parse bio in about section");
            }
        }

        contentBuilder.AppendLine("<br/><hr/><br/>");

        contentBuilder.AppendLine("<h2>ShortBio</h2>");
        foreach (var shortBio in aboutSection.ShortBio)
        {
            try
            {
                if (!string.IsNullOrEmpty(shortBio))
                {
                    contentBuilder.AppendLine($"<p>");
                    contentBuilder.Append(shortBio);
                    contentBuilder.AppendLine($"</p>");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not parse shortBio in about section");
            }
        }

        contentBuilder.AppendLine("<br/><hr/><br/>");

        contentBuilder.AppendLine("<h2>Experience</h2>");
        foreach (var exp in aboutSection.Experience)
        {
            contentBuilder.AppendLine($"<img src='{exp.ImageUrl}'/>");
            contentBuilder.AppendLine($"<h3>{exp.Title}</h3>");
            if (!string.IsNullOrEmpty(exp.Description))
            {
                var bodyHtml = PlateRichTextParser.Parse(exp.Description);
                contentBuilder.AppendLine($"<div>");
                contentBuilder.Append(bodyHtml);
                contentBuilder.AppendLine($"</div>");
            }

            contentBuilder.AppendLine($"<a href='{exp.Link}'>{exp.Link}</a>");
            contentBuilder.AppendLine("<br/><hr/><br/>");
        }

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

        if (thisChannel != null)
        {
            contentBuilder.AppendLine($"<h1>{HttpUtility.HtmlEncode(thisChannel.Name ?? "")}</h1>");
        }

        foreach (var post in posts)
        {
            var content = post.Content;
            if (content == null)
            {
                continue;
            }

            var link = SsrUrlHelper.ToSsrUrl($"/posts/{channelKey}/{content.Slug}");

            contentBuilder.AppendLine("<div>");

            // Title with link
            if (!string.IsNullOrWhiteSpace(content.Caption))
            {
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
                contentBuilder.AppendLine($"<a href=\"{link}\">");
                contentBuilder.AppendLine(
                    $"  <img src=\"{HttpUtility.HtmlEncode(post.ImageUrl)}\" alt=\"{HttpUtility.HtmlEncode(content.Caption)}\" />");
                contentBuilder.AppendLine("</a>");
            }

            contentBuilder.AppendLine("</div>");
            contentBuilder.AppendLine("<hr/>"); // spacing between posts
        }

        CreateMenu(contentBuilder);

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
        contentBuilder.AppendLine($"<h1>{post.Content.Caption}</h1>");
        contentBuilder.AppendLine($"<img src='{post.ImageUrl}' width='600'/>");
        contentBuilder.AppendLine($"<p>{post.Content.UserDate.GetValueOrDefault().ToDateTime()}</p>");
        contentBuilder.AppendLine($"<hr/>");
        try
        {
            var bodyJson = Convert.ToString(post.Content.Body) ?? string.Empty;
            if (!string.IsNullOrEmpty(bodyJson))
            {
                var bodyHtml = PlateRichTextParser.Parse(bodyJson);
                contentBuilder.AppendLine($"<div>");
                contentBuilder.Append(bodyHtml);
                contentBuilder.AppendLine($"</div>");
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
        contentBuilder.AppendLine($"<h3>See More ({otherPosts.Count} posts)</h1>");

        contentBuilder.AppendLine($"<ul>");
        foreach (var anovahPost in otherPosts)
        {
            if (anovahPost?.Content != null &&
                !string.IsNullOrWhiteSpace(anovahPost.Content.Slug) &&
                !string.IsNullOrWhiteSpace(anovahPost.Content.Caption))
            {
                var link = SsrUrlHelper.ToSsrUrl($"/posts/{channelKey}/{anovahPost.Content.Slug}");
                contentBuilder.AppendLine(
                    $"<li><a href=\"{link}\">{HttpUtility.HtmlEncode(anovahPost.Content.Caption)}</a> ({anovahPost.Content.UserDate.GetValueOrDefault().ToDateTime()})</li>");
            }
        }

        contentBuilder.AppendLine($"</ul>");

        CreateMenu(contentBuilder);

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