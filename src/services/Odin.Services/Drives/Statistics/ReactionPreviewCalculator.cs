using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Reactions;
using Odin.Services.Mediator;

namespace Odin.Services.Drives.Statistics;

/// <summary>
/// Listens for reaction file additions/changes and updates their target's preview
/// </summary>
public class ReactionPreviewCalculator(
    FileSystemResolver fileSystemResolver,
    OdinConfiguration config,
    ILogger<ReactionPreviewCalculator> logger)
    : INotificationHandler<IDriveNotification>,
        INotificationHandler<ReactionContentAddedNotification>, INotificationHandler<ReactionContentDeletedNotification>,
        INotificationHandler<AllReactionsByFileDeleted>
{
    public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
    {
        if (notification.IgnoreReactionPreviewCalculation)
        {
            return;
        }

        //TODO: handle encrypted content?
        var odinContext = notification.OdinContext;

        var updatedFileHeader = notification.ServerFileHeader;

        var referencedFile = updatedFileHeader?.FileMetadata?.ReferencedFile;

        if (notification.DriveNotificationType == DriveNotificationType.FileDeleted &&
            !((DriveFileDeletedNotification)notification).IsHardDelete)
        {
            referencedFile = ((DriveFileDeletedNotification)notification).PreviousServerFileHeader.FileMetadata
                .ReferencedFile;
        }

        if (referencedFile == null)
        {
            return;
        }

        // Not the read rule: the summary changes in one read inside storage, as a reaction's does (#2070)
        var (fs, fileId) = await fileSystemResolver.ResolveFileSystem(referencedFile, odinContext);
        if (null == fs || null == fileId)
        {
            return;
        }

        await fs.Storage.ChangeReactionSummary(fileId.Value, stored =>
        {
            var preview = stored ?? new ReactionSummary();
            switch (notification.DriveNotificationType)
            {
                case DriveNotificationType.FileAdded:
                    HandleFileAdded(updatedFileHeader, preview, odinContext);
                    break;
                case DriveNotificationType.FileModified:
                    HandleFileModified(updatedFileHeader, preview, odinContext);
                    break;
                case DriveNotificationType.FileDeleted:
                    HandleFileDeleted(updatedFileHeader, preview);
                    break;
            }

            return preview;
        }, odinContext);
    }

    private void HandleFileDeleted(ServerFileHeader updatedFileHeader,
        ReactionSummary targetFileReactionPreview)
    {
        if (updatedFileHeader.FileMetadata.File.FileId == Guid.Empty)
        {
            logger.LogWarning("ReactionPreviewCalculator.HandleFileDeleted -> FileId is Empty guid; proceeding to check comments. Gtid: {gtid}",
                updatedFileHeader.FileMetadata.GlobalTransitId);
        }
   
        var idx = targetFileReactionPreview.Comments.FindIndex(c =>
            c.FileId == updatedFileHeader.FileMetadata.File.FileId);
        
        if (idx > -1)
        {
            targetFileReactionPreview.Comments.RemoveAt(idx);
            if (targetFileReactionPreview.TotalCommentCount > 0)
            {
                targetFileReactionPreview.TotalCommentCount--;
            }
        }
    }

    private void HandleFileModified(ServerFileHeader updatedFileHeader,
        ReactionSummary targetFileReactionPreview, IOdinContext odinContext)
    {
        if (updatedFileHeader.FileMetadata.File.FileId == Guid.Empty)
        {
            logger.LogError("ReactionPreviewCalculator.HandleFileModified -> FileId is Empty guid; skipping.  Gtid: {gtid}",
                updatedFileHeader.FileMetadata.GlobalTransitId);
            return; 
        }
        
        if (TooLongForPreview(updatedFileHeader))
        {
            return;
        }

        var idx = targetFileReactionPreview.Comments.FindIndex(c =>
            c.FileId == updatedFileHeader.FileMetadata.File.FileId);
        if (idx > -1)
        {
            targetFileReactionPreview.Comments[idx] = new CommentPreview()
            {
                Created = updatedFileHeader.FileMetadata.Created,
                Updated = updatedFileHeader.FileMetadata.Updated,
                OdinId = odinContext.Caller.OdinId,
                IsEncrypted = updatedFileHeader.FileMetadata.IsEncrypted,
                Content = updatedFileHeader.FileMetadata.AppData.Content,
                Reactions = new List<ReactionContentPreview>()
            };
        }
    }

    private void HandleFileAdded(ServerFileHeader updatedFileHeader, ReactionSummary targetFileReactionPreview,
        IOdinContext odinContext)
    {
        if (updatedFileHeader.FileMetadata.File.FileId == Guid.Empty)
        {
            logger.LogError("ReactionPreviewCalculator.HandleFileAdded -> FileId is Empty guid; skipping.  Gtid: {gtid}",
                updatedFileHeader.FileMetadata.GlobalTransitId);
            return; 
        }
        
        //Always increment even if we don't store the contents
        targetFileReactionPreview.TotalCommentCount++;

        if (targetFileReactionPreview.Comments.Count > config.Feed.MaxCommentsInPreview) //TODO: add to config
        {
            return;
        }
        
        if (TooLongForPreview(updatedFileHeader))
        {
            return;
        }

        var isEncrypted = updatedFileHeader.FileMetadata.IsEncrypted;
        targetFileReactionPreview.Comments.Add(new CommentPreview()
        {
            FileId = updatedFileHeader.FileMetadata.File.FileId,
            Created = updatedFileHeader.FileMetadata.Created,
            Updated = updatedFileHeader.FileMetadata.Updated,
            OdinId = odinContext.Caller.OdinId,
            IsEncrypted = isEncrypted,
            Content = isEncrypted ? "" : updatedFileHeader.FileMetadata.AppData.Content,
            Reactions = new List<ReactionContentPreview>()
        });
    }

    private bool TooLongForPreview(ServerFileHeader updatedFileHeader)
    {
        var len = updatedFileHeader.FileMetadata.AppData.Content?.Length ?? 0;
        if (len <= CommentPreview.MaxContentLength)
        {
            return false;
        }

        logger.LogWarning("Skipping adding comment to comment preview due to max " +
                          "size.  Comment content Size: {len}.  Max Preview: {max}",
            len, CommentPreview.MaxContentLength);
        return true;
    }

    public async Task Handle(ReactionContentAddedNotification notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.Reaction.FileId;
        var odinContext = notification.OdinContext;

        var fs = await fileSystemResolver.ResolveFileSystemOfStoredFileAsync(targetFile);
        await fs.Storage.ChangeReactionSummary(targetFile, stored =>
        {
            var preview = stored ?? new ReactionSummary();
            var dict = preview.Reactions ?? new Dictionary<Guid, ReactionContentPreview>();

            var key = ByteArrayUtil.ReduceSHA256Hash(notification.Reaction.ReactionContent);
            if (!dict.TryGetValue(key, out ReactionContentPreview reactionPreview))
            {
                reactionPreview = new ReactionContentPreview();
            }

            reactionPreview.Count++;
            reactionPreview.ReactionContent = notification.Reaction.ReactionContent;
            reactionPreview.Key = key;

            dict[key] = reactionPreview;

            preview.Reactions = dict;
            return preview;
        }, odinContext);
    }

    public async Task Handle(ReactionContentDeletedNotification notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.Reaction.FileId;
        var odinContext = notification.OdinContext;
        var fs = await fileSystemResolver.ResolveFileSystemOfStoredFileAsync(targetFile);
        await fs.Storage.ChangeReactionSummary(targetFile, preview =>
        {
            if (null == preview)
            {
                return null;
            }

            var dict = preview.Reactions ?? new Dictionary<Guid, ReactionContentPreview>();

            var key = ByteArrayUtil.ReduceSHA256Hash(notification.Reaction.ReactionContent);
            if (!dict.TryGetValue(key, out ReactionContentPreview reactionPreview))
            {
                return null;
            }

            reactionPreview.Count--;

            if (reactionPreview.Count == 0)
            {
                dict.Remove(key);
            }
            else
            {
                reactionPreview.ReactionContent = notification.Reaction.ReactionContent;
                reactionPreview.Key = key;
                dict[key] = reactionPreview;
            }

            preview.Reactions = dict;
            return preview;
        }, odinContext);
    }

    public async Task Handle(AllReactionsByFileDeleted notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.FileId;
        var odinContext = notification.OdinContext;
        var fs = await fileSystemResolver.ResolveFileSystemOfStoredFileAsync(targetFile);
        await fs.Storage.ChangeReactionSummary(targetFile, preview =>
        {
            preview?.Reactions?.Clear();
            return preview;
        }, odinContext);
    }
}
