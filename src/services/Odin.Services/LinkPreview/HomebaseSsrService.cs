using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;
using Odin.Services.LinkPreview.Posts;
using Odin.Services.LinkPreview.Profile;
using Odin.Services.Membership.Connections;

namespace Odin.Services.LinkPreview;

public class HomebaseSsrService(
    HomebaseProfileContentService profileContentService,
    FollowerService followerService,
    CircleNetworkService cn,
    HomebaseChannelContentService channelContentService,
    ILogger<HomebaseSsrService> logger)
{
    public void WriteHomeBodyContent(StringBuilder contentBuilder, PersonSchema person, IOdinContext odinContext)
    {
        var imageUrl = profileContentService.GetPublicImageUrl(odinContext);
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

    public async Task WriteConnectionsBodyContent(StringBuilder contentBuilder, PersonSchema person, IOdinContext odinContext)
    {
        contentBuilder.AppendLine($"<h1>{person.Name}</h1>");
        contentBuilder.AppendLine($"<h2>{person.Status}</h2>");

        if (person.BioSummary != null)
        {
            contentBuilder.AppendLine($"<h3>Summary: {person.BioSummary}</h3>");
        }

        var count = Int32.MaxValue;
        if (odinContext.PermissionsContext?.HasPermission(PermissionKeys.ReadConnections) ?? false)
        {
            var result = await cn.GetConnectedIdentitiesAsync(count, null, odinContext);
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

        if (odinContext.PermissionsContext?.HasPermission(PermissionKeys.ReadWhoIFollow) ?? false)
        {
            var peopleIFollow = await followerService.GetIdentitiesIFollowAsync(count, null, odinContext);
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
    }

    public async Task WriteLinksBodyContent(StringBuilder contentBuilder, PersonSchema person)
    {
        var links = await profileContentService.LoadLinks();

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
    }

    public async Task<bool> WriteAboutBodyContent(StringBuilder contentBuilder, PersonSchema person, IOdinContext odinContext)
    {
        contentBuilder.AppendLine($"<h1>{person.Name}</h1>");
        contentBuilder.AppendLine($"<h2>{person.Status}</h2>");

        var aboutSection = await profileContentService.LoadAboutSection(odinContext);
        if (null == aboutSection)
        {
            return false;
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

        return true;
    }

    public async Task WriteSitemap(StringBuilder contentBuilder, IOdinContext odinContext)
    {
        string Template(string path, DateTime lastModified, string freq = "weekly")
        {
            var root = new UriBuilder()
            {
                Scheme = "https",
                Host = odinContext.Tenant,
                Path = path,
            }.Uri;

            var s = $@"
            <url>
                <loc>{root.ToString()}</loc>
                <lastmod>{lastModified:yyyy-MM-dd}</lastmod>
                <changefreq>{freq}</changefreq>
                <priority>1.0</priority>
              </url>";
            return s;
        }

        contentBuilder.AppendLine("<?xml version='1.0' encoding='UTF-8'?>");
        contentBuilder.AppendLine("<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>");

        var lastModified = DateTime.UtcNow;
        contentBuilder.AppendLine(Template("/", lastModified));
        contentBuilder.AppendLine(Template("/connections", lastModified));
        contentBuilder.AppendLine(Template("/links", lastModified));
        contentBuilder.AppendLine(Template("/about", lastModified));
        contentBuilder.AppendLine(Template("/posts", lastModified));

        try
        {
            var channels = await channelContentService.GetChannels(odinContext);

            foreach (var channel in channels)
            {
                var channelKey = channel.Slug;
                contentBuilder.AppendLine(Template($"/posts/{channelKey}", lastModified));
                var (idList, _) = await channelContentService.GetChannelPostIds(channelKey, odinContext, 1000);

                foreach (var (id, modified) in idList)
                {
                    // logger.LogDebug("Content: {id}|{slug}|{caption}", content.Id, content.Slug, content.Caption);
                    contentBuilder.AppendLine(Template(
                        path: $"/posts/{channelKey}/{id}",
                        lastModified: modified.ToDateTime(),
                        freq: "monthly"));
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed rendering posts in sitemap");
        }

        contentBuilder.AppendLine("</urlset>");
    }

    public void WriteChannelPostListBody(string channelKey, ChannelDefinition thisChannel, StringBuilder contentBuilder,
        List<ChannelPost> posts)
    {
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
    }

    public async Task WritePostBodyContent(string channelKey, ChannelPost post,
        StringBuilder contentBuilder,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        contentBuilder.AppendLine($"<h1>{post.Content.Caption}</h1>");
        contentBuilder.AppendLine($"<img src='{post.ImageUrl}' width='600'/>");
        contentBuilder.AppendLine($"<p>{post.Content.Abstract}</p>");
        contentBuilder.AppendLine($"<p>{post.Content.UserDate.GetValueOrDefault().ToDateTime()}</p>");
        contentBuilder.AppendLine($"<hr/>");
        try
        {
            var bodyJson = Convert.ToString(post.Content.Body) ?? string.Empty;
            if (string.IsNullOrEmpty(bodyJson))
            {
                logger.LogDebug("Post body is empty for file {f} on channel [{ck}]", post.FileId, channelKey);
                contentBuilder.AppendLine($"<p>nb</p>");
            }
            else
            {
                var bodyHtml = PlateRichTextParser.Parse(bodyJson);
                contentBuilder.AppendLine($"<div>");
                contentBuilder.Append(bodyHtml);

                // render the reaction preview

                try
                {
                    if (null != post.ReactionSummary)
                    {
                        contentBuilder.AppendLine($"<h3>Comments {post.ReactionSummary.TotalCommentCount}</h3>");
                        contentBuilder.AppendLine("<ul>");
                        foreach (var comment in post.ReactionSummary.Comments)
                        {
                            contentBuilder.AppendLine("<li>");
                            var href = $"<a href='https://{comment.OdinId}'>{comment.OdinId}</a>";
                            contentBuilder.AppendLine($"{href} : {comment.Content} ({comment.Updated.ToDateTime()})");

                            contentBuilder.AppendLine("<span>");
                            foreach (var reaction in comment.Reactions)
                            {
                                contentBuilder.AppendLine($"{reaction.ReactionContent} ({reaction.Count})");
                            }

                            contentBuilder.AppendLine("</span>");

                            contentBuilder.AppendLine("</li>");
                        }

                        contentBuilder.AppendLine("</ul>");
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to render reaction summary");
                }

                contentBuilder.AppendLine($"</div>");
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Failed to Post article body");
        }

        var (otherPosts, _) = await channelContentService.GetChannelPosts(
            channelKey,
            odinContext,
            post.Content.UserDate,
            maxPosts: 10,
            cancellationToken);

        contentBuilder.AppendLine($"<hr/>");
        contentBuilder.AppendLine($"<h3>See More ({otherPosts.Count} posts)</h3>");

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
}