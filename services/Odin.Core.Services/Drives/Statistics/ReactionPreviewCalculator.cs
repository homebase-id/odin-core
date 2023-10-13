using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Mediator;
using Odin.Core.Util;

namespace Odin.Core.Services.Drives.Statistics;

/// <summary>
/// Listens for reaction file additions/changes and updates their target's preview
/// </summary>
public class ReactionPreviewCalculator : INotificationHandler<IDriveNotification>,
    INotificationHandler<ReactionContentAddedNotification>, INotificationHandler<ReactionDeletedNotification>,
    INotificationHandler<AllReactionsByFileDeleted>
{
    private readonly OdinContextAccessor _contextAccessor;
    private readonly FileSystemResolver _fileSystemResolver;

    public ReactionPreviewCalculator(OdinContextAccessor contextAccessor, FileSystemResolver fileSystemResolver)
    {
        _contextAccessor = contextAccessor;
        _fileSystemResolver = fileSystemResolver;
    }

    public async Task Handle(IDriveNotification notification, CancellationToken cancellationToken)
    {
        //TODO: handle encrypted content?

        var updatedFileHeader = notification.ServerFileHeader;
        if (updatedFileHeader?.FileMetadata?.ReferencedFile == null)
        {
            return;
        }

        //look up the fileId by  updatedFileHeader.FileMetadata.ReferencedFile.GlobalTransitId
        var (fs, _) = _fileSystemResolver.ResolveFileSystem(updatedFileHeader.FileMetadata.ReferencedFile).GetAwaiter().GetResult();
        if (null == fs)
        {
            //TODO: consider if we log this or just ignore it
            return;
        }

        // var targetFile = new InternalDriveFileId()
        // {
        //     DriveId = referenceFileDriveId,
        //     FileId = fileId
        // };

        var referencedFile = updatedFileHeader.FileMetadata.ReferencedFile!;
        var referenceFileDriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(referencedFile.TargetDrive);
        var referencedFileHeader = await fs.Query.GetFileByGlobalTransitId(referenceFileDriveId, referencedFile.GlobalTransitId);
        var referencedFileReactionPreview = referencedFileHeader.FileMetadata.ReactionPreview ?? new ReactionSummary();

        if (notification.DriveNotificationType == DriveNotificationType.FileAdded)
        {
            HandleFileAdded(updatedFileHeader, ref referencedFileReactionPreview);
        }

        if (notification.DriveNotificationType == DriveNotificationType.FileModified)
        {
            HandleFileModified(updatedFileHeader, ref referencedFileReactionPreview);
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
            referencedFileReactionPreview);
    }

    private void HandleFileDeleted(ServerFileHeader updatedFileHeader,
        ref ReactionSummary targetFileReactionPreview)
    {
        targetFileReactionPreview.TotalCommentCount--;
        var idx = targetFileReactionPreview.Comments.FindIndex(c =>
            c.FileId == updatedFileHeader.FileMetadata.File.FileId);
        if (idx > -1)
        {
            targetFileReactionPreview.Comments.RemoveAt(idx);
        }
    }

    private void HandleFileModified(ServerFileHeader updatedFileHeader,
        ref ReactionSummary targetFileReactionPreview)
    {
        var idx = targetFileReactionPreview.Comments.FindIndex(c =>
            c.FileId == updatedFileHeader.FileMetadata.File.FileId);

        if (idx > -1)
        {
            targetFileReactionPreview.Comments[idx] = new CommentPreview()
            {
                Created = updatedFileHeader.FileMetadata.Created,
                Updated = updatedFileHeader.FileMetadata.Updated,
                OdinId = _contextAccessor.GetCurrent().Caller.OdinId,
                IsEncrypted = updatedFileHeader.FileMetadata.PayloadIsEncrypted,
                JsonContent = updatedFileHeader.FileMetadata.AppData.JsonContent,
                Reactions = new List<ReactionContentPreview>()
            };
        }
    }

    private void HandleFileAdded(ServerFileHeader updatedFileHeader, ref ReactionSummary targetFileReactionPreview)
    {
        //Always increment even if we don't store the contents
        targetFileReactionPreview.TotalCommentCount++;

        if (targetFileReactionPreview.Comments.Count > 3) //TODO: add to config
        {
            return;
        }

        var isEncrypted = updatedFileHeader.FileMetadata.PayloadIsEncrypted;
        targetFileReactionPreview.Comments.Add(new CommentPreview()
        {
            FileId = updatedFileHeader.FileMetadata.File.FileId,
            Created = updatedFileHeader.FileMetadata.Created,
            Updated = updatedFileHeader.FileMetadata.Updated,
            OdinId = _contextAccessor.GetCurrent().Caller.OdinId,
            IsEncrypted = isEncrypted,
            JsonContent = isEncrypted ? "" : updatedFileHeader.FileMetadata.AppData.JsonContent,
            Reactions = new List<ReactionContentPreview>()
        });
    }

    public Task Handle(ReactionContentAddedNotification notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.Reaction.FileId;
        var fs = _fileSystemResolver.ResolveFileSystem(targetFile);
        var header = fs.Storage.GetServerFileHeader(targetFile).GetAwaiter().GetResult();
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

        fs.Storage.UpdateReactionPreview(targetFile, preview).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    public Task Handle(ReactionDeletedNotification notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.Reaction.FileId;
        var fs = _fileSystemResolver.ResolveFileSystem(targetFile);
        var header = fs.Storage.GetServerFileHeader(targetFile).GetAwaiter().GetResult();
        var preview = header?.FileMetadata.ReactionPreview;

        if (null == preview)
        {
            return Task.CompletedTask;
        }

        var dict = preview.Reactions ?? new Dictionary<Guid, ReactionContentPreview>();

        var key = ByteArrayUtil.ReduceSHA256Hash(notification.Reaction.ReactionContent);
        if (!dict.TryGetValue(key, out ReactionContentPreview reactionPreview))
        {
            return Task.CompletedTask;
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

        fs.Storage.UpdateReactionPreview(targetFile, preview).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    public Task Handle(AllReactionsByFileDeleted notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.FileId;
        var fs = _fileSystemResolver.ResolveFileSystem(targetFile);
        var header = fs.Storage.GetServerFileHeader(targetFile).GetAwaiter().GetResult();
        var preview = header?.FileMetadata.ReactionPreview;

        if (null == preview)
        {
            return Task.CompletedTask;
        }

        if (null != preview.Reactions)
        {
            preview.Reactions.Clear();
        }
        
        fs.Storage.UpdateReactionPreview(targetFile, preview).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }
}