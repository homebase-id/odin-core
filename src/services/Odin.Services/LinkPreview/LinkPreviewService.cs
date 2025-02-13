using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using AngleSharp.Io;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Hosting.PersonMetadata;
using Odin.Hosting.PersonMetadata.SchemaDotOrg;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;
using Odin.Services.LinkPreview.Posts;
using Odin.Services.Optimization.Cdn;

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
    private const int ChannelDefinitionFileType = 103;

    public async Task WriteIndexFileAsync(string indexFilePath, IOdinContext odinContext)
    {
        if (!await TryWritePostPreview(indexFilePath, odinContext))
        {
            await WriteGenericPreview(indexFilePath);
        }
    }

    private async Task<bool> TryWritePostPreview(string indexFilePath, IOdinContext odinContext)
    {
        try
        {
            var context = httpContextAccessor.HttpContext;

            // React route is 
            // <Route path="posts/:channelKey/:postKey" element={<PostDetail />} />

            if (!context.Request.Path.StartsWithSegments("/posts"))
            {
                logger.LogDebug("Path is not a posts path; falling back");
                return false;
            }

            string path = context.Request.Path.Value;
            var segments = path?.TrimEnd('/').Split('/');
            if (segments == null || segments.Length < 3)
            {
                logger.LogDebug("Posts path has too few segments; falling back");
                return false;
            }

            string channelKey = segments[2];
            string postKey = segments[3];

            var (success, title, imageUrl, description) = await TryParsePostFile(channelKey, postKey, odinContext, context.RequestAborted);

            if (!success)
            {
                return false;
            }

            string odinId = context.Request.Host.Host;
            var person = await GeneratePersonSchema();

            if (string.IsNullOrEmpty(title))
            {
                title  = $"{person?.Name ?? odinId} | Homebase";
            }

            if (string.IsNullOrEmpty(imageUrl))
            {
                imageUrl = person?.Image ?? $"{context.Request.Scheme}://{odinId}/pub/image";
            }

            if (string.IsNullOrEmpty(description))
            {
                description = "Decentralized identity powered by Homebase.id";
            }

            var content = await PrepareIndexHtml(indexFilePath, title, imageUrl, description, person, context.RequestAborted);

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
        var (success, targetDrive, driveId) = await TryGetChannelDrive(channelKey, odinContext);
        if (!success)
        {
            return (false, null, null, null);
        }

        var postFile = await FindPost(postKey, odinContext, targetDrive);
        if (null == postFile)
        {
            logger.LogDebug("File for post with postKey {pk} not found", postKey);
            return (false, null, null, null);
        }

        var fileId = new InternalDriveFileId()
        {
            DriveId = driveId.GetValueOrDefault(),
            FileId = postFile.FileId
        };


        PostContent content = null;
        var payloadHeader = postFile.FileMetadata.Payloads.SingleOrDefault(k => k.Key == DefaultPayloadKey);
        if (payloadHeader == null)
        {
            content = OdinSystemSerializer.Deserialize<PostContent>(postFile.FileMetadata.AppData.Content);
            logger.LogDebug("Post content used from AppData.Content");
        }
        else
        {
            // if there is a default payload, then all content is there;
            var payloadStream = await fileSystem.Storage.GetPayloadStreamAsync(fileId, DefaultPayloadKey, null, odinContext);
            content = await OdinSystemSerializer.Deserialize<PostContent>(payloadStream.Stream, cancellationToken);
            payloadStream.Stream.Close();
            logger.LogDebug("Post content used from payload with key {pk}", DefaultPayloadKey);
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

            bool hasUsableThumbnail = mediaPayload?.Thumbnails
                                          .Any(t => t.PixelHeight > minThumbHeight
                                                    && t.PixelWidth > minThumbWidth)
                                      ?? false;
            if (hasUsableThumbnail)
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

                var builder = new UriBuilder(context.Request.Scheme, context.Request.Host.Host)
                {
                    Path = "api/guest/v1/drive/files/thumb",
                    Query = b.ToString()
                };

                imageUrl = builder.ToString();
            }
        }

        logger.LogDebug("Returning post content.  " +
                        "title:[{title}], description: {desc} imageUrl:{img}",
                        content.Caption,
                        imageUrl,
                        content.Abstract);
        
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
            logger.LogDebug("Post with key [{pk}] found using postIdAsTag: {tag}", postKey, postIdAsTag);
        }
        else
        {
            // postKey is a slug so we need to mdf
            var uid = ToGuidId(postKey);
            postFile = await fileSystem.Query.GetFileByClientUniqueId(driveId, uid, odinContext);
            logger.LogDebug("Post with key [{pk}] found using post as Slug: {uid}", postKey, uid);
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

        var cache = await tenantCache.GetOrSetAsync(
            GenericLinkPreviewCacheKey,
            _ => PrepareGenericPreview(indexFilePath, context.RequestAborted),
            TimeSpan.FromSeconds(30)
        );

        await WriteAsync(cache, context.RequestAborted);
    }

    private async Task<string> PrepareGenericPreview(string indexFilePath, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        string odinId = context.Request.Host.Host;
        var person = await GeneratePersonSchema();

        var imageUrl = person?.Image ?? $"{context.Request.Scheme}://{odinId}/pub/image";

        var description = person?.Description ?? "Decentralized identity powered by Homebase.id";
        var title = $"{person?.Name ?? odinId} | Homebase";

        return await PrepareIndexHtml(indexFilePath, title, imageUrl, description, person, cancellationToken);
    }

    private async Task<string> PrepareIndexHtml(string indexFilePath, string title, string imageUrl, string description,
        PersonSchema person, CancellationToken cancellationToken)
    {
        const string placeholder = "@@identifier-content@@";

        StringBuilder b = new StringBuilder(500);

        title = HttpUtility.HtmlEncode(title);
        description = HttpUtility.HtmlEncode(description);

        b.Append($"<title>{title}</title>");
        b.Append($"<meta property='description' content='{description}'/>\n");

        b.Append($"<meta property='og:title' content='{title}'/>\n");
        b.Append($"<meta property='og:description' content='{description}'/>\n");

        b.Append($"<meta property='og:image' content='{imageUrl}'/>\n");
        b.Append($"<meta property='og:url' content='{GetDisplayUrl()}'/>\n");
        b.Append($"<meta property='og:site_name' content='{title}'/>\n");
        b.Append($"<meta property='og:type' content='website'/>\n");

        b.Append(PrepareIdentityContent(person));

        var indexTemplate = await globalCache.GetOrSetAsync(
            IndexFileKey,
            _ => LoadIndexFileTemplate(indexFilePath, cancellationToken),
            TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);

        var updatedContent = indexTemplate.Replace(placeholder, b.ToString());
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
        b.Append($"<link rel='webfinger' content='{context.Request.Scheme}://{odinId}/.well-known/webfinger?resource=acct:@{odinId}'/>\n");
        b.Append("<script type='application/ld+json'>\n");
        b.Append(OdinSystemSerializer.Serialize(person) + "\n");
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
            reader.Close();
            profile = OdinSystemSerializer.Deserialize<FrontEndProfile>(data);
        }

        var person = new PersonSchema
        {
            Name = profile?.Name,
            GivenName = profile?.GiveName,
            FamilyName = profile?.Surname,
            Email = "",
            Description = profile?.Bio,
            BirthDate = "",
            JobTitle = "",
            Image = profile?.Image
            // WorksFor = new OrganizationSchema { Name = "Tech Corp" },
            // Address = new AddressSchema
            // {
            //     StreetAddress = "123 Main St",
            //     AddressLocality = "San Francisco",
            //     AddressRegion = "CA",
            //     PostalCode = "94105",
            //     AddressCountry = "USA"
            // },
            // SameAs = new List<string>
            // {
            //     "https://www.linkedin.com/in/johndoe",
            //     "https://twitter.com/johndoe"
            // }
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
        using (MD5 md5 = MD5.Create())
        {
            byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Convert first 16 bytes of the hash into a GUID
            return new Guid(hashBytes);
        }
    }
}