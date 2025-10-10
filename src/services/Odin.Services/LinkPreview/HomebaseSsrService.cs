using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
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
                contentBuilder.AppendLine(Template($"/posts/{channel.Slug}", lastModified));
                var (posts, _) = await channelContentService.GetChannelPosts(channelKey, odinContext, maxPosts: 1000);

                foreach (var post in posts)
                {
                    var content = post.Content;
                    if (content == null)
                    {
                        continue;
                    }

                    contentBuilder.AppendLine(Template(
                        path: $"/posts/{channelKey}/{content.Slug}",
                        lastModified: post.Modified.ToDateTime(),
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