using System;
using System.IO;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Io;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Hosting.PersonMetadata;
using Odin.Hosting.PersonMetadata.SchemaDotOrg;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.LinkPreview.Posts;
using Odin.Services.Optimization.Cdn;

namespace Odin.Services.LinkPreview;

public class LinkPreviewService(
    IGlobalLevel1Cache globalCache,
    ITenantLevel1Cache<LinkPreviewService> tenantCache,
    StaticFileContentService staticFileContentService,
    IHttpContextAccessor httpContextAccessor,
    StandardFileSystem fileSystem,
    ILogger<LinkPreviewService> logger)
{
    private const string IndexFileKey = "link-preview-service-index-file";
    private const string GenericLinkPreviewCacheKey = "link-preview-service-index-file";
    private const string DefaultPayloadKey = "dflt_key";

    public async Task WriteIndexFileAsync(string indexFilePath, IOdinContext odinContext)
    {
        if (!await TryWritePostPreview(indexFilePath, odinContext))
        {
            await WriteGenericPreview(indexFilePath);
        }
    }

    private async Task<bool> TryWritePostPreview(string indexFilePath, IOdinContext odinContext)
    {
        var context = httpContextAccessor.HttpContext;

        // React route is 
        // <Route path="posts/:channelKey/:postKey" element={<PostDetail />} />

        if (!context.Request.Path.StartsWithSegments("/posts"))
        {
            return false;
        }

        string path = context.Request.Path.Value;
        var segments = path?.TrimEnd('/').Split('/');
        if (segments == null || segments.Length < 3)
        {
            return false;
        }

        string channelKey = segments[1];
        string postKey = segments[2];

        var targetDrive = new TargetDrive()
        {
            Alias = ToGuidId(channelKey),
            Type = SystemDriveConstants.ChannelDriveType
        };

        if (!odinContext.PermissionsContext.HasDriveId(targetDrive, out var driveId))
        {
            logger.LogDebug("link preview does not have access to drive for channel-key: {ck}; " +
                            "falling back to generic preview", channelKey);
            return false;
        }


        var (success, title, imageUrl, description) = await TryParsePostFile(driveId, postKey, odinContext, context.RequestAborted);

        if (!success)
        {
            return false;
        }

        string odinId = context.Request.Host.Host;
        var person = await GeneratePersonSchema();

        title ??= $"{person?.Name ?? odinId} | Homebase";
        description ??= "Decentralized identity powered by Homebase.id";
        imageUrl ??= person?.Image ?? $"{context.Request.Scheme}://{odinId}/pub/image";

        var content = await PrepareIndexHtml(indexFilePath, title, imageUrl, description, person, context.RequestAborted);

        await WriteAsync(content, context.RequestAborted);
        return true;
    }

    private async Task<(bool success, string title, string imageUrl, string description)> TryParsePostFile(Guid? driveId, string postKey,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        var postUid = ToGuidId(postKey);
        var postFile = await fileSystem.Query.GetFileByClientUniqueId(driveId.GetValueOrDefault(), postUid, odinContext, true);

        var fileId = new InternalDriveFileId()
        {
            DriveId = driveId.GetValueOrDefault(),
            FileId = postFile.FileId
        };

        var payloadStream = await fileSystem.Storage.GetPayloadStreamAsync(fileId, DefaultPayloadKey, null, odinContext);
        var content = await OdinSystemSerializer.Deserialize<PostContent>(payloadStream.Stream, cancellationToken);
        payloadStream.Stream.Close();

        // type is tweet or article data is small enough, there is no payload for the content.
        // so you must read the json header and the payload with key 'dflt_key'


        if (content.IsPostType(PostType.Article))
        {

            var title = "";
            var imageUrl = ""; //TODO: lookup primary media file
            return (true, title, imageUrl, content.Abstract);

        }

        if (content.IsPostType(PostType.Tweet))
        {
            var title = "";
            var imageUrl = ""; //TODO: lookup primary media file
            return (true, title, imageUrl, content.Body);
        }
        
        return (false, null, null, null);

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

        var context = httpContextAccessor.HttpContext;

        StringBuilder b = new StringBuilder(500);

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
        return $"{request.Scheme}://{request.Host}/{request.Path}/?{request.QueryString}";
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