using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
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
public class ReactionPreviewCalculator(FileSystemResolver fileSystemResolver, OdinConfiguration config)
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

        //look up the fileId by  updatedFileHeader.FileMetadata.ReferencedFile.GlobalTransitId
        var (fs, fileId) = await fileSystemResolver.ResolveFileSystem(referencedFile, odinContext, notification.db);
        if (null == fs || null == fileId)
        {
            //TODO: consider if we log this or just ignore it
            return;
        }

        // var targetFile = new InternalDriveFileId()
        // {
        //     DriveId = referenceFileDriveId,
        //     FileId = fileId
        // };

        // var referencedFile = updatedFileHeader.FileMetadata.ReferencedFile!;
        var referenceFileDriveId = odinContext.PermissionsContext.GetDriveId(referencedFile.TargetDrive);
        var referencedFileHeader = await fs.Query.GetFileByGlobalTransitId(referenceFileDriveId,
            referencedFile.GlobalTransitId, odinContext, notification.db);
        var referencedFileReactionPreview = referencedFileHeader.FileMetadata.ReactionPreview ?? new ReactionSummary();

        if (notification.DriveNotificationType == DriveNotificationType.FileAdded)
        {
            HandleFileAdded(updatedFileHeader, ref referencedFileReactionPreview, odinContext);
        }

        if (notification.DriveNotificationType == DriveNotificationType.FileModified)
        {
            HandleFileModified(updatedFileHeader, ref referencedFileReactionPreview, odinContext);
        }

        if (notification.DriveNotificationType == DriveNotificationType.FileDeleted)
        {
            HandleFileDeleted(updatedFileHeader, ref referencedFileReactionPreview);
        }

        await fs.Storage.UpdateReactionPreview(new InternalDriveFileId()
            {
                FileId = referencedFileHeader.FileId,
                DriveId = referenceFileDriveId
            },
            referencedFileReactionPreview,
            odinContext,
            notification.db);
    }

    private void HandleFileDeleted(ServerFileHeader updatedFileHeader,
        ref ReactionSummary targetFileReactionPreview)
    {
        if (targetFileReactionPreview.TotalCommentCount > 0)
        {
            targetFileReactionPreview.TotalCommentCount--;
        }

        var idx = targetFileReactionPreview.Comments.FindIndex(c =>
            c.FileId == updatedFileHeader.FileMetadata.File.FileId);

        if (idx > -1)
        {
            targetFileReactionPreview.Comments.RemoveAt(idx);
        }
    }

    private void HandleFileModified(ServerFileHeader updatedFileHeader,
        ref ReactionSummary targetFileReactionPreview, IOdinContext odinContext)
    {
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

    private void HandleFileAdded(ServerFileHeader updatedFileHeader, ref ReactionSummary targetFileReactionPreview,
        IOdinContext odinContext)
    {
        //Always increment even if we don't store the contents
        targetFileReactionPreview.TotalCommentCount++;

        if (targetFileReactionPreview.Comments.Count > config.Feed.MaxCommentsInPreview) //TODO: add to config
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

    public async Task Handle(ReactionContentAddedNotification notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.Reaction.FileId;
        var odinContext = notification.OdinContext;
        var fs = await fileSystemResolver.ResolveFileSystem(targetFile, odinContext, notification.db);
        var header = await fs.Storage.GetServerFileHeader(targetFile, odinContext, notification.db);
        var preview = header.FileMetadata.ReactionPreview ?? new ReactionSummary();

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

        await fs.Storage.UpdateReactionPreview(targetFile, preview, odinContext, notification.db);
    }

    public async Task Handle(ReactionContentDeletedNotification notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.Reaction.FileId;
        var odinContext = notification.OdinContext;
        var fs = await fileSystemResolver.ResolveFileSystem(targetFile, odinContext, notification.db);
        var header = await fs.Storage.GetServerFileHeader(targetFile, odinContext, notification.db);
        var preview = header?.FileMetadata.ReactionPreview;

        if (null == preview)
        {
            return;
        }

        var dict = preview.Reactions ?? new Dictionary<Guid, ReactionContentPreview>();

        var key = ByteArrayUtil.ReduceSHA256Hash(notification.Reaction.ReactionContent);
        if (!dict.TryGetValue(key, out ReactionContentPreview reactionPreview))
        {
            return;
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

        await fs.Storage.UpdateReactionPreview(targetFile, preview, odinContext, notification.db);
    }

    public async Task Handle(AllReactionsByFileDeleted notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.FileId;
        var odinContext = notification.OdinContext;
        var fs = await fileSystemResolver.ResolveFileSystem(targetFile, odinContext, notification.db);
        var header = await fs.Storage.GetServerFileHeader(targetFile, odinContext, notification.db);
        var preview = header?.FileMetadata.ReactionPreview;

        if (null == preview)
        {
            return;
        }

        if (null != preview.Reactions)
        {
            preview.Reactions.Clear();
        }

        await fs.Storage.UpdateReactionPreview(targetFile, preview, odinContext, notification.db);
    }
}