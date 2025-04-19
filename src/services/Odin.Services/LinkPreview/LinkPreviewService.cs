using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using AngleSharp.Io;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.LinkPreview.PersonMetadata;
using Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;
using Odin.Services.LinkPreview.Posts;
using Odin.Services.Optimization.Cdn;
using Org.BouncyCastle.Ocsp;

namespace Odin.Services.LinkPreview;

public class LinkPreviewService(
    IGlobalLevel1Cache globalCache,
    ITenantLevel1Cache<LinkPreviewService> tenantCache,
    StaticFileContentService staticFileContentService,
    IHttpContextAccessor httpContextAccessor,
    StandardFileSystem fileSystem,
    DriveManager driveManager,
    ILogger<LinkPreviewService> logger)
{
    private const string IndexFileKey = "link-preview-service-index-file";
    private const string GenericLinkPreviewCacheKey = "link-preview-service-index-file";
    private const string DefaultPayloadKey = "dflt_key";

    private const string DefaultTitle = "Homebase.id";
    private const string DefaultDescription = "Decentralized identity powered by Homebase.id";

    public const string PublicImagePath = "pub/image.jpg";
    const string IndexPlaceholder = "<!-- @@identifier-content@@ -->";
    const string NoScriptPlaceholder = "<!-- @@noscript-identifier-content@@ -->";

    private const int MaxDescriptionLength = 155;

    private const int ChannelDefinitionFileType = 103;

    public async Task WriteIndexFileAsync(string indexFilePath, IOdinContext odinContext)
    {
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            try
            {
                await WriteEnhancedIndexAsync(indexFilePath, odinContext);
            }
            catch (OperationCanceledException ex)
            {
                logger.LogError(ex, "Timeout of 2 seconds; falling back - Writing plain index");
                await WriteFallbackIndex(indexFilePath);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Total Failure creating link-preview.  Writing plain index");
                await WriteFallbackIndex(indexFilePath);
            }
        }
    }

    private async Task WriteEnhancedIndexAsync(string indexFilePath, IOdinContext odinContext)
    {
        if (await TryWritePostPreview(indexFilePath, odinContext))
        {
            return;
        }

        await WriteGenericPreview(indexFilePath);
    }

    public bool IsPostPath()
    {
        return IsPath("/posts");
    }

    private bool IsPath(string path)
    {
        var context = httpContextAccessor.HttpContext;
        return context.Request.Path.StartsWithSegments(path);
    }

    private async Task<bool> TryWritePostPreview(string indexFilePath, IOdinContext odinContext)
    {
        try
        {
            var context = httpContextAccessor.HttpContext;

            // React route is
            // <Route path="posts/:channelKey/:postKey" element={<PostDetail />} />

            if (!IsPostPath())
            {
                logger.LogDebug("Path is not a posts path; falling back");
                return false;
            }

            string path = context.Request.Path.Value;
            var segments = path?.TrimEnd('/').Split('/');

            string odinId = context.Request.Host.Host;

            string description = DefaultDescription;
            string imageUrl = null;
            string title = null;

            if (segments is { Length: >= 4 }) // we have channel key and post key; get the post info
            {
                // segments[0] = ""  from the leading slash
                // segments[1] = "posts"
                string channelKey = segments[2];
                string postKey = segments[3];

                (var success, title, imageUrl, description) = await TryParsePostFile(channelKey, postKey, odinContext,
                    context.RequestAborted);

                if (!success)
                {
                    return false;
                }
            }

            var person = await GeneratePersonSchema();

            if (title == null)
                title = $"{person?.Name ?? odinId} | Posts";

            if (string.IsNullOrEmpty(imageUrl))
            {
                imageUrl = $"{context.Request.Scheme}://{odinId}/{PublicImagePath}";
            }

            if (string.IsNullOrEmpty(description))
            {
                description = DefaultDescription;
            }

            var content = await PrepareIndexHtml(indexFilePath, title, imageUrl,
                description,
                person,
                siteType: "website",
                robotsTag: "index, follow",
                context.RequestAborted);

            await WriteAsync(content, context.RequestAborted);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to parse post");
            return false;
        }
    }

    private async Task<(bool success, string title, string imageUrl, string description)> TryParsePostFile(
        string channelKey,
        string postKey,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Try parse post file with channel key: [{ck}] postKey: [{pk}]", channelKey, postKey);

        var (success, targetDrive, driveId) = await TryGetChannelDrive(channelKey, odinContext);
        if (!success)
        {
            return (false, null, null, null);
        }

        var postFile = await FindPost(postKey, odinContext, targetDrive);
        if (null == postFile)
        {
            logger.LogDebug("File for channelKey:[{ck}] and with postKey {pk} not found", channelKey, postKey);
            return (false, null, null, null);
        }

        var fileId = new InternalDriveFileId()
        {
            DriveId = driveId.GetValueOrDefault(),
            FileId = postFile.FileId
        };


        PostContent content = null;
        var payloadHeader = postFile.FileMetadata.Payloads.SingleOrDefault(k => k.KeyEquals(DefaultPayloadKey));
        var json = "";
        try
        {
            if (payloadHeader == null)
            {
                logger.LogDebug("Using content used from AppData.Content");
                json = postFile.FileMetadata.AppData.Content;
            }
            else
            {
                // if there is a default payload, then all content is there;
                logger.LogDebug("Post content used from payload with key {pk}", DefaultPayloadKey);
                using var payloadStream = await fileSystem.Storage.GetPayloadStreamAsync(fileId, DefaultPayloadKey, null, odinContext);
                using var reader = new StreamReader(payloadStream.Stream);
                json = await reader.ReadToEndAsync(cancellationToken);
            }

            content = OdinSystemSerializer.Deserialize<PostContent>(json);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed deserializing post content. json: [{json}]", json);
            throw;
        }

        var context = httpContextAccessor.HttpContext;

        const int idealWidth = 1200;
        const int idealHeight = 650;

        const int minThumbWidth = 200;
        const int minThumbHeight = 200;

        string imageUrl = null;

        if (content.PrimaryMediaFile?.FileKey != null)
        {
            var mediaPayload = postFile.FileMetadata.Payloads
                .SingleOrDefault(p => p.Key == content.PrimaryMediaFile.FileKey);

            var theThumbnail = mediaPayload?.Thumbnails.OrderBy(t => t.PixelWidth)
                .LastOrDefault(t => t.PixelHeight > minThumbHeight
                                    && t.PixelWidth > minThumbWidth);

            if (theThumbnail != null)
            {
                logger.LogDebug("Post has usable thumbnail");

                StringBuilder b = new StringBuilder(100);
                b.Append($"&alias={targetDrive.Alias}");
                b.Append($"&type={targetDrive.Type}");
                b.Append($"&fileId={postFile.FileId}");
                b.Append($"&payloadKey={content.PrimaryMediaFile.FileKey}");
                b.Append($"&width={idealWidth}&height={idealHeight}");
                b.Append($"&lastModified={mediaPayload?.LastModified.milliseconds}");
                b.Append($"&xfst=Standard"); // note: No comment support
                b.Append($"&iac=true");

                var extension = MimeTypeHelper.GetFileExtensionFromMimeType(theThumbnail.ContentType) ?? ".jpg";

                var builder = new UriBuilder(context.Request.Scheme, context.Request.Host.Host)
                {
                    Path = $"api/guest/v1/drive/files/thumb{extension}",
                    Query = b.ToString()
                };

                imageUrl = builder.ToString();
            }
        }

        // logger.LogDebug("Returning post content.  " +
        //                 "title:[{title}], description: {desc} imageUrl:{img}",
        //     content.Caption,
        //     imageUrl,
        //     content.Abstract);

        return (true, content.Caption, imageUrl, content.Abstract);
    }

    private async Task<SharedSecretEncryptedFileHeader> FindPost(string postKey, IOdinContext odinContext, TargetDrive targetDrive)
    {
        var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);

        SharedSecretEncryptedFileHeader postFile;
        // if post is a guid, it is the Tag on a file
        if (Guid.TryParse(postKey, out var postIdAsTag))
        {
            postFile = await QueryBatchFirstFile(targetDrive, odinContext, postIdAsTag);
            logger.LogDebug("Searching for post with key [{pk}] using postIdAsTag: [{tag}] result:  {result}",
                postKey,
                postIdAsTag,
                postFile == null ? "not found" : "found");
        }
        else
        {
            // postKey is a slug so we need to md5
            var uid = ToGuidId(postKey);
            var options = new ResultOptions
            {
                MaxRecords = 1,
                IncludeHeaderContent = true,
                ExcludePreviewThumbnail = true,
                ExcludeServerMetaData = true,
                IncludeTransferHistory = false
            };

            postFile = await fileSystem.Query.GetFileByClientUniqueId(driveId, uid, options, odinContext);
            logger.LogDebug("Searching for post with key [{pk}] using post as Slug: {uid}] result: {result}",
                postKey,
                uid,
                postFile == null ? "not found" : "found");
        }

        return postFile;
    }

    private async Task<SharedSecretEncryptedFileHeader> QueryBatchFirstFile(TargetDrive targetDrive, IOdinContext odinContext,
        Guid? postIdAsTag = null, int? fileType = null)
    {
        var qp = new FileQueryParams
        {
            TargetDrive = targetDrive,
            TagsMatchAtLeastOne = postIdAsTag == null ? default : [postIdAsTag.GetValueOrDefault()],
            FileType = fileType == null ? default : [fileType.GetValueOrDefault()]
        };

        var options = new QueryBatchResultOptions
        {
            MaxRecords = 1,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = true,
            IncludeTransferHistory = false,
        };

        var driveId = odinContext.PermissionsContext.GetDriveId(targetDrive);
        var result = await fileSystem.Query.GetBatch(driveId, qp, options, odinContext);
        return result.SearchResults.FirstOrDefault();
    }

    private async Task<(bool success, TargetDrive targetDrive, Guid? driveId)> TryGetChannelDrive(string channelKey,
        IOdinContext odinContext)
    {
        TargetDrive targetDrive = null;
        if (Guid.TryParse(channelKey, out var channelId))
        {
            // fetch by id; use the channelId directly as drive alias
            targetDrive = new TargetDrive()
            {
                Alias = channelId,
                Type = SystemDriveConstants.ChannelDriveType
            };
        }
        else
        {
            //look up slug
            // get the channel drive on all drives of type SystemDriveConstants.ChannelDriveType
            //chnl.fileMetadata.appData.content.slug === channelKey

            var channelDrivesPaging = await driveManager.GetDrivesAsync(
                SystemDriveConstants.ChannelDriveType, PageOptions.All, odinContext);

            foreach (var drive in channelDrivesPaging.Results)
            {
                var file = await QueryBatchFirstFile(drive.TargetDriveInfo, odinContext, fileType: ChannelDefinitionFileType);
                if (null != file)
                {
                    var postContent = OdinSystemSerializer.Deserialize<PostContent>(file.FileMetadata.AppData.Content);
                    if (channelKey!.Equals(postContent.Slug, StringComparison.InvariantCultureIgnoreCase))
                    {
                        targetDrive = file.TargetDrive;
                        break;
                    }
                }
            }

            // slug was not found
            if (targetDrive == null)
            {
                logger.LogDebug("Channel key {ck} was not found on any channel drives", channelKey);
                return (false, null, null);
            }
        }

        if (!odinContext.PermissionsContext.HasDriveId(targetDrive, out var driveId))
        {
            logger.LogDebug("link preview does not have access to drive for channel-key: {ck}; " +
                            "falling back to generic preview", channelKey);
            return (false, null, null);
        }

        logger.LogDebug("TargetDrive {td} found by channelKey: {ck}", targetDrive.ToString(), channelKey);
        return (true, targetDrive, driveId);
    }

    private async Task WriteGenericPreview(string indexFilePath)
    {
        var context = httpContextAccessor.HttpContext;

        // var cache = await tenantCache.GetOrSetAsync(
        //     GenericLinkPreviewCacheKey,
        //     _ => PrepareGenericPreview(indexFilePath, context.RequestAborted),
        //     TimeSpan.FromSeconds(30)
        // );
        //
        // await WriteAsync(cache, context.RequestAborted);

        var data = await PrepareGenericPreview(indexFilePath, context.RequestAborted);
        await WriteAsync(data, context.RequestAborted);
    }

    private async Task WriteFallbackIndex(string indexFilePath)
    {
        var context = httpContextAccessor.HttpContext;

        var cache = await tenantCache.GetOrSetAsync(
            GenericLinkPreviewCacheKey,
            _ => PrepareIndex(indexFilePath, context.RequestAborted),
            TimeSpan.FromSeconds(30)
        );

        await WriteAsync(cache, context.RequestAborted);
    }

    private async Task<string> PrepareIndex(string indexFilePath, CancellationToken cancellationToken)
    {
        var indexTemplate = await globalCache.GetOrSetAsync(
            IndexFileKey,
            _ => LoadIndexFileTemplate(indexFilePath, cancellationToken),
            TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(indexTemplate))
        {
            throw new OdinSystemException("index contents read from cache or disk is empty");
        }

        var markup = PrepareHeadBuilder(DefaultTitle, DefaultDescription, "website");
        var updatedContent = indexTemplate.Replace(IndexPlaceholder, markup.ToString());
        return updatedContent;
    }

    private StringBuilder PrepareHeadBuilder(string title, string description, string siteType)
    {
        description = Truncate(description, MaxDescriptionLength);
        title = Truncate(title, MaxDescriptionLength);

        title = HttpUtility.HtmlEncode(title);
        description = HttpUtility.HtmlEncode(description);

        StringBuilder b = new StringBuilder(500);

        // Generic data primarily for SEO
        b.Append($"<title>{title}</title>\n");
        b.Append($"<meta name='description' content='{description}'/>\n");

        // Open Graph attributes
        b.Append($"<meta property='og:title' content='{title}'/>\n");
        b.Append($"<meta property='og:description' content='{description}'/>\n");
        b.Append($"<meta property='og:url' content='{GetDisplayUrl()}'/>\n");
        b.Append($"<meta property='og:site_name' content='{title}'/>\n");
        b.Append($"<meta property='og:type' content='{siteType}'/>\n");

        return b;
    }

    private StringBuilder PrepareNoscriptBuilder(string title, string description, string siteType)
    {
        title = HttpUtility.HtmlEncode(title);
        description = HttpUtility.HtmlEncode(description);

        StringBuilder b = new StringBuilder(500);

        b.Append($"<h1>{title}</h1>\n");
        b.Append($"<p>You need to enable JavaScript to run this app.</p>");
        b.Append($"<p>{description}</p>");

        return b;
    }

    private async Task<string> PrepareGenericPreview(string indexFilePath, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        string odinId = context.Request.Host.Host;

        var imageUrl = $"{context.Request.Scheme}://{odinId}/{PublicImagePath}";
        var person = await GeneratePersonSchema();

        string suffix = DefaultTitle;
        string siteType = "profile";
        string robotsTag = "index, follow";

        if (IsPath("/links"))
        {
            suffix = "Links";
        }

        if (IsPath("/about"))
        {
            suffix = "About";
        }

        if (IsPath("/connections"))
        {
            suffix = "Connections";
        }

        if (IsPath("/preview"))
        {
            robotsTag = "noindex, nofollow";
        }

        var title = $"{person?.Name ?? odinId} | {suffix}";
        var description = person?.Description ?? DefaultDescription;
        return await PrepareIndexHtml(indexFilePath, title, imageUrl, description, person, siteType, robotsTag, cancellationToken);
    }

    private async Task<string> PrepareIndexHtml(string indexFilePath, string title, string imageUrl, string description,
        PersonSchema person, string siteType, string robotsTag, CancellationToken cancellationToken)
    {
        var builder = PrepareHeadBuilder(title, description, siteType);
        builder.Append($"<meta property='og:image' content='{imageUrl}'/>\n");
        builder.Append($"<link rel='canonical' href='{GetDisplayUrl()}' />\n");
        builder.Append($"<meta name='robots' content='{robotsTag}'/>\n");

        builder.Append(PrepareIdentityContent(person));

        var indexTemplate = await globalCache.GetOrSetAsync(
            IndexFileKey,
            _ => LoadIndexFileTemplate(indexFilePath, cancellationToken),
            TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);

        var noScriptContent = PrepareNoscriptBuilder(title, description, siteType);
        var updatedContent = indexTemplate.Replace(IndexPlaceholder, builder.ToString())
            .Replace(NoScriptPlaceholder, noScriptContent.ToString());

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

    private string GetDisplayUrl()
    {
        var request = httpContextAccessor.HttpContext.Request;
        return new UriBuilder(request.Scheme, request.Host.Host)
        {
            Path = request.Path,
            Query = request.QueryString.Value
        }.ToString();
    }

    private async Task<PersonSchema> GeneratePersonSchema()
    {
        // read the profile file.
        var (_, fileExists, fileStream) =
            await staticFileContentService.GetStaticFileStreamAsync(StaticFileConstants.PublicProfileCardFileName);

        FrontEndProfile profile = null;

        if (fileExists)
        {
            using var reader = new StreamReader(fileStream);
            var data = await reader.ReadToEndAsync();
            profile = OdinSystemSerializer.Deserialize<FrontEndProfile>(data);
        }

        var context = httpContextAccessor.HttpContext;
        string odinId = context.Request.Host.Host;

        var person = new PersonSchema
        {
            Name = profile?.Name,
            GivenName = profile?.GiveName,
            FamilyName = profile?.FamilyName,
            Email = null,
            Description = profile?.BioSummary ?? profile?.Bio,
            BirthDate = null,
            JobTitle = null,
            Image = AppendJpgIfNoExtension(profile?.Image ?? ""),
            SameAs = profile?.SameAs?.Select(s => s.Url).ToList() ?? [],
            Identifier =
            [
                $"{context.Request.Scheme}://{odinId}/.well-known/webfinger?resource=acct:@{odinId}",
                $"{context.Request.Scheme}://{odinId}/.well-known/did.json"
            ]
        };
        return person;
    }

    private async Task<string> LoadIndexFileTemplate(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return content;
    }

    private async Task WriteAsync(string cache, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        context.Response.Headers[HeaderNames.ContentType] = MediaTypeNames.Text.Html;
        await context.Response.WriteAsync(cache, cancellationToken);
    }

    private static Guid ToGuidId(string input)
    {
        using MD5 md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

        var b = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        return new Guid(b);
    }

    //via gpt 
    private static string AppendJpgIfNoExtension(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
        {
            return url;
        }

        string path = uri.AbsolutePath;

        if (string.IsNullOrEmpty(Path.GetExtension(path)))
        {
            string newPath = path + ".jpg";

            UriBuilder builder = new UriBuilder(uri)
            {
                Path = newPath
            };

            return builder.Uri.ToString();
        }

        return url;
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
}