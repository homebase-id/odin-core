using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.PublicPage.PersonMetadata.SchemaDotOrg;
using Odin.Services.PublicPage.Posts;
using Odin.Services.PublicPage.Profile;
using Odin.Services.PublicPage.Seo;
using Odin.Services.Membership.Connections;

namespace Odin.Services.PublicPage;

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
        contentBuilder.AppendLine($"<img src='{E(imageUrl)}' width='600'/>");
        WriteProfileHeader(contentBuilder, person);

        if (person.Bio != null)
        {
            contentBuilder.AppendLine($"<hr/>");
            contentBuilder.AppendLine($"<p>Bio: {E(person.Bio)}</p>");
        }

        CreateMenu(contentBuilder);
    }

    public async Task WriteConnectionsBodyContent(StringBuilder contentBuilder, PersonSchema person, IOdinContext odinContext)
    {
        WriteProfileHeader(contentBuilder, person);

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
                contentBuilder.AppendLine($"<img src=\"{E(imageUrl)}\" alt=\"Status\" width=\"24\" height=\"24\"" +
                                          $" style=\"vertical-align: middle; margin-right: 8px;\"/>");
                contentBuilder.AppendLine($"<a href='{E($"https://{odinId}/ssr")}'>{E(odinId)}</a>");

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

                contentBuilder.AppendLine($"<img src=\"{E(imageUrl)}\" alt=\"Status\" width=\"24\" " +
                                          $"height=\"24\" style=\"vertical-align: middle; margin-right: 8px;\"/>");
                contentBuilder.AppendLine($"<a href='{E($"https://{odinId}/ssr")}'>{E(odinId)}</a>");
                contentBuilder.AppendLine("  </li>");
            }

            contentBuilder.AppendLine("</ul>");
        }

        CreateMenu(contentBuilder);
    }

    public async Task WriteLinksBodyContent(StringBuilder contentBuilder, PersonSchema person)
    {
        var links = await profileContentService.LoadLinks();

        WriteProfileHeader(contentBuilder, person);

        contentBuilder.AppendLine("<h3>My Links</h3>");
        contentBuilder.AppendLine("<ul>");
        foreach (var link in links)
        {
            contentBuilder.AppendLine("  <li>");
            contentBuilder.AppendLine($"    {Link(link.Url, link.Type)}");
            contentBuilder.AppendLine("  </li>");
        }

        contentBuilder.AppendLine("</ul>");
        CreateMenu(contentBuilder);
    }

    public async Task<bool> WriteAboutBodyContent(StringBuilder contentBuilder, PersonSchema person, IOdinContext odinContext)
    {
        WriteProfileHeader(contentBuilder, person, withSummary: false);

        var aboutSection = await profileContentService.LoadAboutSection(odinContext);
        if (null == aboutSection)
        {
            return false;
        }

        contentBuilder.AppendLine("<br/><hr/><br/>");

        contentBuilder.AppendLine("<h2>Status</h2>");
        foreach (var status in aboutSection.Status)
        {
            contentBuilder.AppendLine($"<p>{E(status)}</p>");
        }

        contentBuilder.AppendLine("<br/><hr/><br/>");

        contentBuilder.AppendLine("<h2>Bio</h2>");
        foreach (var bio in aboutSection.Bio)
        {
            try
            {
                if (!string.IsNullOrEmpty(bio))
                {
                    var bodyHtml = PlateRichTextParser.Parse(HtmlSafe.ParseJson(bio));
                    contentBuilder.AppendLine($"<div>");
                    contentBuilder.Append(bodyHtml);
                    contentBuilder.AppendLine($"</div>");
                }
            }
            catch (JsonException) // this fallback is for older bios that have not been changed to json format.
            {
                contentBuilder.AppendLine($"<div>");
                contentBuilder.Append(E(bio));
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
                    contentBuilder.Append(E(shortBio));
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
            if (HtmlSafe.SafeUrl(exp.ImageUrl) is { } imageUrl)
            {
                contentBuilder.AppendLine($"<img src='{E(imageUrl)}'/>");
            }

            contentBuilder.AppendLine($"<h3>{E(exp.Title)}</h3>");
            if (!string.IsNullOrEmpty(exp.Description))
            {
                var bodyHtml = PlateRichTextParser.Parse(HtmlSafe.ParseJson(exp.Description));
                contentBuilder.AppendLine($"<div>");
                contentBuilder.Append(bodyHtml);
                contentBuilder.AppendLine($"</div>");
            }

            contentBuilder.AppendLine(Link(exp.Link, exp.Link));
            contentBuilder.AppendLine("<br/><hr/><br/>");
        }

        CreateMenu(contentBuilder);

        return true;
    }

    private const int MaxFeedItems = 20;

    /// <summary>RSS 2.0 feed of the latest public articles across all channels</summary>
    public async Task<string> BuildFeed(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var tenant = odinContext.Tenant.DomainName;
        var person = await profileContentService.LoadPersonSchema();
        var items = new List<RssItem>();

        // as the sitemap: a failure leaves a partial (cached) feed, not an error
        List<ChannelDefinition> channels = [];
        try
        {
            channels = await channelContentService.GetChannels(odinContext);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Failed loading the channels for the feed");
        }

        foreach (var channel in channels)
        {
            // one bad channel must not take the others with it
            try
            {
                // by drive alias, as a slug would scan every channel drive again
                var (posts, _) = await channelContentService.GetChannelPosts(
                    channel.DriveAlias.ToString(), odinContext, maxPosts: MaxFeedItems, includePayloadBody: false,
                    cancellationToken: cancellationToken, dataType: HomebaseChannelContentService.ArticleDataType);

                foreach (var post in posts.Where(p => p.Content.IsArticle))
                {
                    var slug = post.Content.Slug?.Trim();
                    var id = string.IsNullOrEmpty(slug) || Guid.TryParse(slug, out _) ? post.FileId.ToString() : slug;
                    items.Add(new RssItem(MarkdownText.ToTitle(post.Content.Caption), RssFeed.PostUrl(tenant, channel.Slug, id),
                        post.FileId.ToString(), ArticleBodyRenderer.Description(post.Content), post.PublishedDate.ToDateTimeOffset()));
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Failed rendering posts of channel {slug} in feed", channel.Slug);
            }
        }

        return RssFeed.Build(
            person?.Name ?? tenant,
            $"https://{tenant}/",
            person?.BioSummary ?? LinkPreviewDefaults.DefaultDescription,
            $"https://{tenant}/feed.xml",
            items.OrderByDescending(i => i.Published).Take(MaxFeedItems));
    }

    public async Task WriteSitemap(StringBuilder contentBuilder, IOdinContext odinContext)
    {
        // AbsoluteUri keeps an escaped segment escaped, which a <loc> needs (ToString() would unescape a slug's '&')
        string Page(string path) => new UriBuilder { Scheme = "https", Host = odinContext.Tenant, Path = path }.Uri.AbsoluteUri;

        string Template(string loc, DateTime lastModified, string freq = "weekly")
        {
            var s = $@"
            <url>
                <loc>{loc}</loc>
                <lastmod>{lastModified:yyyy-MM-dd}</lastmod>
                <changefreq>{freq}</changefreq>
                <priority>1.0</priority>
              </url>";
            return s;
        }

        contentBuilder.AppendLine("<?xml version='1.0' encoding='UTF-8'?>");
        contentBuilder.AppendLine("<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>");

        var lastModified = DateTime.UtcNow;
        contentBuilder.AppendLine(Template(Page("/"), lastModified));
        contentBuilder.AppendLine(Template(Page("/connections"), lastModified));
        contentBuilder.AppendLine(Template(Page("/links"), lastModified));
        contentBuilder.AppendLine(Template(Page("/about"), lastModified));
        contentBuilder.AppendLine(Template(Page("/posts"), lastModified));

        try
        {
            var channels = await channelContentService.GetChannels(odinContext);

            foreach (var channel in channels)
            {
                var channelKey = channel.Slug;
                contentBuilder.AppendLine(Template(Page(ChannelPath(channelKey)), lastModified));
                var (idList, _) = await channelContentService.GetChannelPostIds(channelKey, odinContext, 1000);

                foreach (var (id, modified) in idList)
                {
                    // logger.LogDebug("Content: {id}|{slug}|{caption}", content.Id, content.Slug, content.Caption);
                    contentBuilder.AppendLine(Template(
                        loc: RssFeed.PostUrl(odinContext.Tenant, channelKey, id),
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
            contentBuilder.AppendLine($"<h1>{E(thisChannel.Name ?? "")}</h1>");
        }

        foreach (var post in posts)
        {
            var content = post.Content;
            if (content == null)
            {
                continue;
            }

            var link = PostLink(channelKey, content.Slug);

            contentBuilder.AppendLine("<div>");

            var title = MarkdownText.ToTitle(content.Caption);

            // Title with link
            if (title.Length > 0)
            {
                contentBuilder.AppendLine($"  <h3><a href=\"{E(link)}\">{E(title)}</a></h3>");
            }

            // Abstract
            if (!string.IsNullOrWhiteSpace(content.Abstract))
            {
                contentBuilder.AppendLine($"  <p>{E(content.Abstract)}</p>");
            }

            // Image
            if (!string.IsNullOrWhiteSpace(post.ImageUrl) && title.Length > 0)
            {
                contentBuilder.AppendLine($"<a href=\"{E(link)}\">");
                contentBuilder.AppendLine(
                    $"  <img src=\"{E(post.ImageUrl)}\" alt=\"{E(title)}\" />");
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
        WritePostArticle(channelKey, post, contentBuilder);

        var (otherPosts, _) = await channelContentService.GetChannelPosts(
            channelKey,
            odinContext,
            post.Content.UserDate,
            maxPosts: 10,
            includePayloadBody: false, // "See More" list only needs caption/slug/date from the header
            cancellationToken: cancellationToken);

        contentBuilder.AppendLine($"<hr/>");
        contentBuilder.AppendLine($"<h3>See More ({otherPosts.Count} posts)</h3>");

        contentBuilder.AppendLine($"<ul>");
        foreach (var anovahPost in otherPosts)
        {
            var title = MarkdownText.ToTitle(anovahPost?.Content?.Caption);
            if (anovahPost?.Content != null &&
                !string.IsNullOrWhiteSpace(anovahPost.Content.Slug) &&
                title.Length > 0)
            {
                var link = PostLink(channelKey, anovahPost.Content.Slug);
                contentBuilder.AppendLine(
                    $"<li><a href=\"{E(link)}\">{E(title)}</a> ({anovahPost.Content.UserDate.GetValueOrDefault().ToDateTime()})</li>");
            }
        }

        contentBuilder.AppendLine($"</ul>");

        CreateMenu(contentBuilder);
    }

    /// <summary>
    /// The post itself: caption, image, abstract, date, body and comment preview. Every value from the post is encoded.
    /// </summary>
    internal void WritePostArticle(string channelKey, ChannelPost post, StringBuilder contentBuilder)
    {
        contentBuilder.AppendLine($"<h1>{E(MarkdownText.ToPlain(post.Content.Caption)).Replace("\n", "<br/>")}</h1>");
        if (!string.IsNullOrWhiteSpace(post.ImageUrl))
        {
            contentBuilder.AppendLine($"<img src='{E(post.ImageUrl)}' width='600'/>");
        }

        contentBuilder.AppendLine($"<p>{E(post.Content.Abstract)}</p>");
        var published = post.PublishedDate.ToDateTimeOffset().UtcDateTime;
        contentBuilder.AppendLine(
            $"<p><time datetime='{published:s}Z'>{published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}</time></p>");
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
                var bodyHtml = ArticleBodyRenderer.Render(bodyJson, post.Content.BodyDoc, post.PayloadUrl);
                contentBuilder.AppendLine($"<div>");
                contentBuilder.Append(bodyHtml);
                contentBuilder.AppendLine($"</div>");
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to render the body of post {f} on channel [{ck}]", post.FileId, channelKey);
        }

        // the reaction preview renders even when the body could not
        try
        {
            if (null != post.ReactionSummary)
            {
                contentBuilder.AppendLine($"<h3>Comments {post.ReactionSummary.TotalCommentCount}</h3>");
                contentBuilder.AppendLine("<ul>");
                foreach (var comment in post.ReactionSummary.Comments ?? [])
                {
                    contentBuilder.AppendLine("<li>");
                    var href = $"<a href='{E($"https://{comment.OdinId}")}'>{E(comment.OdinId)}</a>";
                    contentBuilder.AppendLine($"{href} : {E(comment.Content)} ({comment.Updated.ToDateTime()})");

                    contentBuilder.AppendLine("<span>");
                    foreach (var reaction in comment.Reactions ?? [])
                    {
                        contentBuilder.AppendLine($"{E(reaction.ReactionContent)} ({reaction.Count})");
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
    }

    /// <summary>Encodes text or a quoted attribute value</summary>
    private static string E(string s) => HtmlSafe.Enc(s);

    /// <summary>The profile's name, status and, unless <paramref name="withSummary"/> is false, its summary</summary>
    internal static void WriteProfileHeader(StringBuilder contentBuilder, PersonSchema person, bool withSummary = true)
    {
        contentBuilder.AppendLine($"<h1>{E(person.Name)}</h1>");
        contentBuilder.AppendLine($"<h2>{E(person.Status)}</h2>");
        if (withSummary && person.BioSummary != null)
        {
            contentBuilder.AppendLine($"<h3>Summary: {E(person.BioSummary)}</h3>");
        }
    }

    /// <summary>A link to an http(s) URL the owner wrote; any other (javascript:, data:) is shown as its text only</summary>
    internal static string Link(string url, string text) =>
        HtmlSafe.SafeUrl(url) is { } safe ? $"<a href='{E(safe)}'>{E(text)}</a>" : E(text);

    /// <summary>A channel's page, its client-written slug escaped as a path segment</summary>
    internal static string ChannelLink(string channelKey) => SsrUrlHelper.ToSsrUrl(ChannelPath(channelKey));

    /// <summary>A post's page: the channel and the client-written slug escaped as path segments</summary>
    internal static string PostLink(string channelKey, string slug) =>
        SsrUrlHelper.ToSsrUrl($"{ChannelPath(channelKey)}/{Uri.EscapeDataString(slug ?? "")}");

    private static string ChannelPath(string channelKey) => $"/posts/{Uri.EscapeDataString(channelKey ?? "")}";

    internal static void CreateMenu(StringBuilder contentBuilder)
    {
        contentBuilder.AppendLine($"<ul>");
        contentBuilder.AppendLine($"<li><a href='{SsrUrlHelper.ToSsrUrl("posts")}'>See my Posts</a></li>");
        contentBuilder.AppendLine($"<li><a href='{SsrUrlHelper.ToSsrUrl("connections")}'>See my connections</a></li>");
        contentBuilder.AppendLine($"<li><a href='{SsrUrlHelper.ToSsrUrl("about")}'>About me</a></li>");
        contentBuilder.AppendLine($"<li><a href='{SsrUrlHelper.ToSsrUrl("links")}'>See my links</a></li>");
        contentBuilder.AppendLine($"</ul>");
    }
}