using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using AngleSharp.Io;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Services.Base;
using Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;
using Odin.Services.LinkPreview.Posts;
using Odin.Services.LinkPreview.Profile;

namespace Odin.Services.LinkPreview;

public class LinkPreviewService(
    ISystemLevel1Cache globalCache,
    ITenantLevel1Cache<LinkPreviewService> tenantCache,
    HomebaseProfileContentService profileContentService,
    HomebaseChannelContentService channelContentService,
    IHttpContextAccessor httpContextAccessor,
    HomebaseSsrService ssrService,
    ILogger<LinkPreviewService> logger)
{
    private const string IndexFileKey = "link-preview-service-index-file";
    private const string GenericLinkPreviewCacheKey = "link-preview-service-index-file";

    const string IndexPlaceholder = "<!-- @@identifier-content@@ -->";
    const string NoScriptPlaceholder = "<!-- @@noscript-identifier-content@@ -->";

    public async Task WriteIndexFileAsync(string indexFilePath, IOdinContext odinContext)
    {
        try
        {
            await WriteEnhancedIndexAsync(indexFilePath, odinContext);
        }
        catch (Exception e)
        {
            logger.LogDebug("Total Failure creating link-preview.  Writing plain index: {message}", e.Message);
            await WriteFallbackIndex(indexFilePath);
        }
    }

    private async Task WriteEnhancedIndexAsync(string indexFilePath, IOdinContext odinContext)
    {
        if (await TryWritePostPreview(indexFilePath, odinContext))
        {
            return;
        }

        if (await TryWriteChannelPreview(indexFilePath, odinContext))
        {
            return;
        }

        await WriteGenericPreview(indexFilePath, odinContext);
    }

    private bool IsPostPath()
    {
        return IsPath("/posts");
    }

    private bool IsPath(string path)
    {
        var context = httpContextAccessor.HttpContext;
        return context.Request.Path.StartsWithSegments(path);
    }

    private async Task<bool> TryWriteChannelPreview(string indexFilePath, IOdinContext odinContext)
    {
        try
        {
            var (success, channelKey, thisChannel, posts) = await TryGetChannelData(odinContext);

            if (!success)
            {
                return false;
            }

            var context = httpContextAccessor.HttpContext;
            string odinId = context.Request.Host.Host;
            var person = await GeneratePersonSchema();

            var title = thisChannel.Name;
            var imageUrl = $"{context.Request.Scheme}://{odinId}/{LinkPreviewDefaults.PublicImagePath}";
            var description = thisChannel.Description ?? LinkPreviewDefaults.DefaultDescription;

            StringBuilder noScriptContentBuilder = new StringBuilder();
            ssrService.WriteChannelPostListBody(channelKey, thisChannel, noScriptContentBuilder, posts);

            var content = await PrepareIndexHtml(indexFilePath,
                title,
                imageUrl,
                description,
                person,
                siteType: "website",
                robotsTag: "index, follow",
                noScriptContent: noScriptContentBuilder.ToString(),
                odinContext);

            await WriteAsync(content);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to parse channel definition");
            return false;
        }
    }

    private async Task<bool> TryWritePostPreview(string indexFilePath, IOdinContext odinContext)
    {
        try
        {
            var (success, description, imageUrl, title, channelKey, postContent) = await TryGetPostData(odinContext);

            if (!success)
            {
                return false;
            }

            var context = httpContextAccessor.HttpContext;
            string odinId = context.Request.Host.Host;
            var person = await GeneratePersonSchema();

            if (title == null)
            {
                title = $"{person?.Name ?? odinId} | Posts";
            }

            if (string.IsNullOrEmpty(imageUrl))
            {
                imageUrl = $"{context.Request.Scheme}://{odinId}/{LinkPreviewDefaults.PublicImagePath}";
            }

            if (string.IsNullOrEmpty(description))
            {
                description = LinkPreviewDefaults.DefaultDescription;
            }

            StringBuilder noScriptContentBuilder = new StringBuilder();

            await ssrService.WritePostBodyContent(channelKey, postContent, noScriptContentBuilder, odinContext, context.RequestAborted);

            var content = await PrepareIndexHtml(indexFilePath, title, imageUrl,
                description,
                person,
                siteType: "website",
                robotsTag: "index, follow",
                noScriptContent: noScriptContentBuilder.ToString(),
                odinContext);

            await WriteAsync(content);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to parse post");
            return false;
        }
    }

    private async Task<(bool, string channelKey, ChannelDefinition thisChannel, List<ChannelPost> posts)>
        TryGetChannelData(IOdinContext odinContext)
    {
        if (!IsPostPath())
        {
            return (false, null, null, null);
        }

        var context = httpContextAccessor.HttpContext;
        string path = context.Request.Path.Value;
        var segments = path?.TrimEnd('/').Split('/');
        if (segments is { Length: 3 }) // we have the channel key
        {
            string channelKey = segments[2];
            var (posts, _) = await channelContentService.GetChannelPosts(channelKey, odinContext);
            var thisChannel = (await channelContentService.GetChannels(odinContext)).FirstOrDefault(c => c.Slug == channelKey);

            return (true, channelKey, thisChannel, posts);
        }

        return (false, null, null, null);
    }

    private async Task<(bool success, string description, string imageUrl, string title, string channelKey, ChannelPost content)>
        TryGetPostData(IOdinContext odinContext)
    {
        var context = httpContextAccessor.HttpContext;

        // React route is
        // <Route path="posts/:channelKey/:postKey" element={<PostDetail />} />

        if (!IsPostPath())
        {
            // logger.LogDebug("Path is not a posts path; falling back");
            return (false, null, null, null, null, null);
        }

        string path = context.Request.Path.Value;
        var segments = path?.TrimEnd('/').Split('/');
        if (segments is { Length: >= 4 }) // we have channel key and post key; get the post info
        {
            // segments[0] = ""  from the leading slash
            // segments[1] = "posts"
            string channelKey = segments[2];
            string postKey = segments[3];

            var (success, title, imageUrl, description, content) = await TryParsePostFile(channelKey, postKey, odinContext,
                context.RequestAborted);
            return (success, description, imageUrl, title, channelKey, content);
        }

        return (false, null, null, null, null, null);
    }

    private async Task<(bool success, string title, string imageUrl, string description, ChannelPost channelPost)> TryParsePostFile(
        string channelKey,
        string postKey,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        var post = await channelContentService.GetPost(channelKey, postKey, odinContext, cancellationToken);
        if (null == post)
        {
            return (false, null, null, null, null);
        }

        PostContent content = post.Content;
        string imageUrl = post.ImageUrl;
        return (true, content.Caption, imageUrl, content.Abstract, post);
    }

    private async Task WriteGenericPreview(string indexFilePath, IOdinContext odinContext)
    {
        var data = await PrepareGenericPreview(indexFilePath, odinContext);
        await WriteAsync(data);
    }

    private async Task WriteFallbackIndex(string indexFilePath)
    {
        var cache = await tenantCache.GetOrSetAsync(
            GenericLinkPreviewCacheKey,
            _ => PrepareIndex(indexFilePath),
            TimeSpan.FromSeconds(30)
        );

        await WriteAsync(cache);
    }

    private async Task<string> PrepareIndex(string indexFilePath)
    {
        var indexTemplate = await globalCache.GetOrSetAsync(
            IndexFileKey,
            _ => LoadIndexFileTemplate(indexFilePath),
            TimeSpan.FromSeconds(30));

        if (string.IsNullOrEmpty(indexTemplate))
        {
            throw new OdinSystemException("index contents read from cache or disk is empty");
        }

        var markup = PrepareHeadBuilder(LinkPreviewDefaults.DefaultTitle, LinkPreviewDefaults.DefaultDescription, "website");
        var updatedContent = indexTemplate.Replace(IndexPlaceholder, markup.ToString());
        return updatedContent;
    }

    private StringBuilder PrepareHeadBuilder(string title, string description, string siteType)
    {
        description = Truncate(description, LinkPreviewDefaults.MaxDescriptionLength);
        title = Truncate(title, LinkPreviewDefaults.MaxDescriptionLength);

        title = HttpUtility.HtmlEncode(title);
        description = HttpUtility.HtmlEncode(description);

        StringBuilder b = new StringBuilder(500);

        // Generic data primarily for SEO
        b.Append($"<title>{title}</title>\n");
        b.Append($"<meta name='description' content='{description}'/>\n");

        // Open Graph attributes
        b.Append($"<meta property='og:title' content='{title}'/>\n");
        b.Append($"<meta property='og:description' content='{description}'/>\n");
        b.Append($"<meta property='og:url' content='{GetHumanReadableVersion(httpContextAccessor.HttpContext)}'/>\n");
        b.Append($"<meta property='og:site_name' content='{title}'/>\n");
        b.Append($"<meta property='og:type' content='{siteType}'/>\n");

        return b;
    }

    private async Task<string> PrepareGenericPreview(string indexFilePath, IOdinContext odinContext)
    {
        var context = httpContextAccessor.HttpContext;
        string odinId = context.Request.Host.Host;

        var imageUrl = $"{context.Request.Scheme}://{odinId}/{LinkPreviewDefaults.PublicImagePath}";

        var person = await GeneratePersonSchema();

        string suffix = LinkPreviewDefaults.DefaultTitle;
        string siteType = "profile";
        string robotsTag = "index, follow";
        var description = DataOrNull(person?.BioSummary) ?? Truncate(DataOrNull(person?.Bio)) ?? LinkPreviewDefaults.DefaultDescription;

        StringBuilder noScriptContentBuilder = new StringBuilder();
        if (IsPath("/"))
        {
            ssrService.WriteHomeBodyContent(noScriptContentBuilder, person, odinContext);
        }

        if (IsPath("/links"))
        {
            suffix = "Links";
            await ssrService.WriteLinksBodyContent(noScriptContentBuilder, person);
        }

        if (IsPath("/about"))
        {
            suffix = "About";
            await ssrService.WriteAboutBodyContent(noScriptContentBuilder, person, odinContext);
        }

        if (IsPath("/connections"))
        {
            suffix = "Connections";
            await ssrService.WriteConnectionsBodyContent(noScriptContentBuilder, person, odinContext);
        }

        if (IsPath("/preview"))
        {
            robotsTag = "noindex, nofollow";
        }

        var title = $"{person?.Name ?? odinId} | {suffix}";
        noScriptContentBuilder.AppendLine($"<a href='{GetDisplayUrlWithSsr(httpContextAccessor.HttpContext)}'>");
        noScriptContentBuilder.AppendLine(HttpUtility.HtmlEncode(title));
        noScriptContentBuilder.AppendLine("</a>");

        return await PrepareIndexHtml(indexFilePath, title, imageUrl, description, person, siteType,
            robotsTag,
            noScriptContent: noScriptContentBuilder.ToString(),
            odinContext);
    }

    private string DataOrNull(string data)
    {
        return string.IsNullOrEmpty(data) ? null : data;
    }

    private string Truncate(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        return data.Substring(0, 160);
    }


    private async Task<string> PrepareIndexHtml(string indexFilePath, string title, string imageUrl, string description,
        PersonSchema person, string siteType, string robotsTag, string noScriptContent, IOdinContext odinContext)
    {
        var builder = PrepareHeadBuilder(title, description, siteType);
        builder.Append($"<meta property='og:image' content='{imageUrl}'/>\n");
        builder.Append($"<link rel='alternate' href='{GetDisplayUrlWithSsr(httpContextAccessor.HttpContext)}' />\n");
        builder.Append($"<link rel='canonical' href='{GetHumanReadableVersion(httpContextAccessor.HttpContext)}' />\n");

        builder.Append(PrepareIdentityContent(person));

        var indexTemplate = await globalCache.GetOrSetAsync(
            IndexFileKey,
            _ => LoadIndexFileTemplate(indexFilePath),
            TimeSpan.FromSeconds(30));

        var updatedContent = indexTemplate.Replace(IndexPlaceholder, builder.ToString())
            .Replace(NoScriptPlaceholder, noScriptContent);

        return updatedContent;
    }

    private string PrepareIdentityContent(PersonSchema person)
    {
        StringBuilder b = new StringBuilder(500);

        var context = httpContextAccessor.HttpContext;
        string odinId = context.Request.Host.Host;

        b.Append($"<meta property='profile:first_name' content='{person?.GivenName}'/>\n");
        b.Append($"<meta property='profile:last_name' content='{person?.FamilyName}'/>\n");
        b.Append($"<meta property='profile:username' content='{context.Request.Host}'/>\n");
        b.Append($"<link rel='webfinger' href='{context.Request.Scheme}://{odinId}/.well-known/webfinger?resource=acct:@{odinId}'/>\n");
        b.Append($"<link rel='did' href='{context.Request.Scheme}://{odinId}/.well-known/did.json'/>\n");
        b.Append("<script type='application/ld+json'>\n");

        var options = new JsonSerializerOptions(OdinSystemSerializer.JsonSerializerOptions!)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        b.Append(OdinSystemSerializer.Serialize(person, options) + "\n");
        b.Append("</script>");

        return b.ToString();
    }

    private static string GetHumanReadableVersion(HttpContext httpContext)
    {
        var request = httpContext.Request;
        var path = request.Path.HasValue ? request.Path.Value : "";
        return new UriBuilder(request.Scheme, request.Host.Host)
        {
            Path = path.Replace($"/{LinkPreviewDefaults.SsrPath}", "", StringComparison.OrdinalIgnoreCase),
            Query = request.QueryString.Value ?? ""
        }.ToString();
    }

    private static string GetDisplayUrlWithSsr(HttpContext httpContext)
    {
        var request = httpContext.Request;
        var originalPath = request.Path.Value ?? "";

        // Avoid double /ssr prefix
        var ssrPrefix = $"/{LinkPreviewDefaults.SsrPath}";
        var pathWithSsr = originalPath.StartsWith(ssrPrefix, StringComparison.OrdinalIgnoreCase)
            ? originalPath
            : $"{ssrPrefix}{originalPath}";

        return new UriBuilder(request.Scheme, request.Host.Host)
        {
            Path = pathWithSsr,
            Query = request.QueryString.Value ?? ""
        }.ToString();
    }

    private async Task<PersonSchema> GeneratePersonSchema()
    {
        return await profileContentService.LoadPersonSchema();
    }

    private async Task<string> LoadIndexFileTemplate(string path)
    {
        var content = await File.ReadAllTextAsync(path);
        return content;
    }

    private async Task WriteAsync(string content)
    {
        var context = httpContextAccessor.HttpContext;
        context.Response.Headers[HeaderNames.ContentType] = MediaTypeNames.Text.Html;
        try
        {
            await context.Response.WriteAsync(content);
        }
        catch (IOException)
        {
            // ignore - socket closed
        }
        catch (OperationCanceledException)
        {
            // ignore - cancelled
        }
    }

    public static string Truncate(string input, int maxLength)
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

    public bool IsAllowedPath()
    {
        List<string> allowedPaths =
        [
            $"/{LinkPreviewDefaults.SsrPath}",
            "/posts",
            "/connections",
            "/links",
            "/about",
            "/sitemap.xml"
        ];

        return allowedPaths.Any(IsPath);
    }
}