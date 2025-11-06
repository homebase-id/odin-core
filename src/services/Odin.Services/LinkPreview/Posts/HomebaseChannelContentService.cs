using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Time;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.Management;

namespace Odin.Services.LinkPreview.Posts;

/// <summary>
/// Loads content from the /home app for channels, posts, articles. (duplicates logic from the Frontend,
/// so we can work w/ the content server side)
/// </summary>
public class HomebaseChannelContentService(
    IHttpContextAccessor httpContextAccessor,
    StandardFileSystem fileSystem,
    IDriveManager driveManager,
    ILogger<HomebaseChannelContentService> logger)
{
    private const string DefaultPayloadKey = "dflt_key";
    private const int PostFileType = 101;
    private const int ChannelDefinitionFileType = 103;
    private const string PostFullTextPayloadKey = "pst_text";

    public async Task<ChannelPost> GetPost(
        string channelKey,
        string postKey,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        var targetDrive = await GetChannelDrive(channelKey, odinContext);
        var postFile = await FindPostFile(postKey, odinContext, targetDrive);
        if (null == postFile)
        {
            return null;
        }

        return await ParsePostFile(postFile, targetDrive, odinContext, cancellationToken);
    }

    /// <summary>
    /// Gets a list of channels
    /// </summary>
    public async Task<List<ChannelDefinition>> GetChannels(IOdinContext odinContext)
    {
        // get all channels drives
        var permissibleChannels = await GetPermissibleChannelDrives(odinContext);

        var results = new List<ChannelDefinition>();

        foreach (var channel in permissibleChannels)
        {
            var targetDrive = channel.TargetDriveInfo;
            var file = await QueryBatchFirstFile(targetDrive, odinContext, fileType: ChannelDefinitionFileType);
            if (null != file)
            {
                results.Add(OdinSystemSerializer.Deserialize<ChannelDefinition>(file.FileMetadata.AppData.Content));
            }
        }

        return results;
    }

    public async Task<(List<ChannelPost> channelPosts, string cursor)> GetChannelPosts(
        string channelKey,
        IOdinContext odinContext,
        UnixTimeUtc? fromTimestamp = null,
        int maxPosts = 10,
        CancellationToken cancellationToken = default)
    {
        var targetDrive = await GetChannelDrive(channelKey, odinContext);

        var qp = new FileQueryParams
        {
            TargetDrive = targetDrive,
            FileType = [PostFileType]
        };

        var options = new QueryBatchResultOptions
        {
            MaxRecords = maxPosts,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = true,
            IncludeTransferHistory = false,
            Sorting = QueryBatchSortField.UserDate,
            Ordering = QueryBatchSortOrder.NewestFirst,
            Cursor = fromTimestamp == null ? null : QueryBatchCursor.FromStartPoint(fromTimestamp.GetValueOrDefault())
        };

        var batch = await fileSystem.Query.GetBatch(driveId: targetDrive.Alias, qp, options, odinContext);

        var channelPosts = new List<ChannelPost>();
        foreach (var sr in batch.SearchResults)
        {
            // logger.LogDebug("The DSR content: [{c}]", sr.FileMetadata.AppData.Content);
            var post = await ParsePostFile(sr, targetDrive, odinContext, cancellationToken);
            channelPosts.Add(post);
        }

        return (channelPosts, batch.Cursor?.pagingCursor?.Time.milliseconds.ToString() ?? "");
    }

    public async Task<(List<(string id, UnixTimeUtc modified)> channelPosts, string cursor)> GetChannelPostIds(
        string channelKey,
        IOdinContext odinContext,
        int maxPosts,
        CancellationToken cancellationToken = default)
    {
        var targetDrive = await GetChannelDrive(channelKey, odinContext);

        var qp = new FileQueryParams
        {
            TargetDrive = targetDrive,
            FileType = [PostFileType]
        };

        var options = new QueryBatchResultOptions
        {
            MaxRecords = maxPosts,
            IncludeHeaderContent = true,
            ExcludePreviewThumbnail = true,
            ExcludeServerMetaData = true,
            IncludeTransferHistory = false,
            Sorting = QueryBatchSortField.UserDate,
            Ordering = QueryBatchSortOrder.NewestFirst,
            Cursor = null
        };

        var batch = await fileSystem.Query.GetBatch(driveId: targetDrive.Alias, qp, options, odinContext);

        logger.LogDebug("Processing posts for channel: [{ck}]", channelKey);

        var list = new List<(string, UnixTimeUtc Updated)>();
        foreach (var postHeader in batch.SearchResults)
        {
            var content = postHeader.FileMetadata.AppData.Content;
            var pc = OdinSystemSerializer.Deserialize<PostContent>(content);

            logger.LogDebug("Raw post content for fileId:{fid} [{pc}]", postHeader.FileId, content);

            var slug = pc?.Slug?.Trim();
            var id = slug ?? postHeader.FileId.ToString();

            // if the slug is a guid, we will use the fileid.  this accounts for a bug on the FE
            if (Guid.TryParse(slug, out _))
            {
                id = postHeader.FileId.ToString();
            }

            logger.LogDebug("For fileId: {fix}; using Id:{id} ", postHeader.FileId, id);
            list.Add(
                (
                    id,
                    postHeader.FileMetadata.Updated
                )
            );
        }

        return (list, batch.Cursor?.pagingCursor?.Time.milliseconds.ToString() ?? "");
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

        var result = await fileSystem.Query.GetBatch(driveId: targetDrive.Alias, qp, options, odinContext);
        return result.SearchResults.FirstOrDefault();
    }

    private async Task<SharedSecretEncryptedFileHeader> FindPostFile(string postKey, IOdinContext odinContext, TargetDrive targetDrive)
    {
        SharedSecretEncryptedFileHeader postFile;
        // if post is a guid, it is the Tag on a file
        if (Guid.TryParse(postKey, out var postIdAsTag))
        {
            postFile = await QueryBatchFirstFile(targetDrive, odinContext, postIdAsTag);
            // logger.LogDebug("Searching for post with key [{pk}] using postIdAsTag: [{tag}] result:  {result}",
            //     postKey,
            //     postIdAsTag,
            //     postFile == null ? "not found" : "found");
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

            postFile = await fileSystem.Query.GetFileByClientUniqueId(driveId: targetDrive.Alias, uid, options, odinContext);
            // logger.LogDebug("Searching for post with key [{pk}] using post as Slug: {uid}] result: {result}",
            //     postKey,
            //     uid,
            //     postFile == null ? "not found" : "found");
        }

        return postFile;
    }

    private async Task<TargetDrive> GetChannelDrive(string channelKey, IOdinContext odinContext)
    {
        TargetDrive targetDrive = null;
        if (Guid.TryParse(channelKey, out var channelId))
        {
            odinContext.PermissionsContext.AssertHasDrivePermission(channelId, DrivePermission.Read);

            // fetch by id; use the channelId directly as drive alias
            targetDrive = new TargetDrive()
            {
                Alias = channelId,
                Type = SystemDriveConstants.ChannelDriveType
            };
            return targetDrive;
        }

        //look up slug
        // get the channel drive on all drives of type SystemDriveConstants.ChannelDriveType
        //chnl.fileMetadata.appData.content.slug === channelKey

        var channelDrivesPage = await driveManager.GetDrivesAsync(
            SystemDriveConstants.ChannelDriveType, PageOptions.All, odinContext);

        foreach (var drive in channelDrivesPage.Results)
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
            throw new OdinClientException("invalid channel");
        }

        return targetDrive;
    }

    private async Task<IEnumerable<StorageDrive>> GetPermissibleChannelDrives(IOdinContext odinContext)
    {
        var channelDrivesPage = await driveManager.GetDrivesAsync(
            SystemDriveConstants.ChannelDriveType, PageOptions.All, odinContext);

        var permissibleChannels = channelDrivesPage.Results.Where(
            d => odinContext.PermissionsContext.HasDrivePermission(d.Id, DrivePermission.Read));
        return permissibleChannels;
    }

    private async Task<ChannelPost> ParsePostFile(
        SharedSecretEncryptedFileHeader postFile,
        TargetDrive channelDrive,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        async Task<PostContent> LoadBodyFromPayload(InternalDriveFileId fileId)
        {
            using var payloadStream = await fileSystem.Storage.GetPayloadStreamAsync(fileId, PostFullTextPayloadKey, null, odinContext);
            using var reader = new StreamReader(payloadStream.Stream);
            var json = await reader.ReadToEndAsync(cancellationToken);
            return OdinSystemSerializer.DeserializeOrThrow<PostContent>(json);
        }

        async Task<PostContent> LoadContentFromPayload(InternalDriveFileId fileId)
        {
            using var payloadStream = await fileSystem.Storage.GetPayloadStreamAsync(fileId, DefaultPayloadKey, null, odinContext);
            using var reader = new StreamReader(payloadStream.Stream);
            var json = await reader.ReadToEndAsync(cancellationToken);
            return OdinSystemSerializer.DeserializeOrThrow<PostContent>(json);
        }

        var fileId = new InternalDriveFileId()
        {
            DriveId = channelDrive.Alias,
            FileId = postFile.FileId
        };

        PostContent content = null;
        var payloadHeader = postFile.FileMetadata.Payloads.SingleOrDefault(k => k.KeyEquals(DefaultPayloadKey));

        try
        {
            content = OdinSystemSerializer.Deserialize<PostContent>(postFile.FileMetadata.AppData.Content);

            if (content == null)
            {
                logger.LogDebug("Failed deserializing post content from header.  Will try loading from default key({key}). json: [{json}]",
                    DefaultPayloadKey,
                    postFile.FileMetadata.AppData.Content);
                
                content = await LoadContentFromPayload(fileId);
            }
            else
            {
                if (postFile.FileMetadata.Payloads.Any(p => p.KeyEquals(PostFullTextPayloadKey)))
                {
                    var bodyFromPayload = await LoadBodyFromPayload(fileId);
                    content.Body = bodyFromPayload.Body;
                }
            }
        }
        catch (Exception e)
        {
            // if incomplete and there is a payload try parsing that
            logger.LogError(e, "Failed deserializing post content from header. json: [{json}]", postFile.FileMetadata.AppData.Content);

            if (payloadHeader != null)
            {
                // if there is a default payload, then all content is there;
                // logger.LogDebug("Post content used from payload with key {pk}", DefaultPayloadKey);
                content = await LoadContentFromPayload(fileId);
            }
        }

        if (null == content)
        {
            throw new OdinSystemException("Could not parse post content");
        }

        content.UserDate = postFile.FileMetadata.AppData.UserDate;

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
                // logger.LogDebug("Post has usable thumbnail");

                StringBuilder b = new StringBuilder(100);
                b.Append($"&alias={channelDrive.Alias}");
                b.Append($"&type={channelDrive.Type}");
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

        var post = new ChannelPost()
        {
            FileId = fileId.FileId,
            Content = content,
            ImageUrl = imageUrl,
            Modified = postFile.FileMetadata.Updated,
            ReactionSummary = postFile.FileMetadata.ReactionPreview
        };

        return post;
    }

    private static Guid ToGuidId(string input)
    {
        using MD5 md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

        var b = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        return new Guid(b);
    }
}