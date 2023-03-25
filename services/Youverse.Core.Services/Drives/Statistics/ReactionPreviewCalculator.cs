using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Base;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Drives.Statistics;

/// <summary>
/// Listens for reaction file additions/changes and updates their target's preview
/// </summary>
public class ReactionPreviewCalculator : INotificationHandler<IDriveNotification>,
    INotificationHandler<ReactionContentAddedNotification>
{
    private readonly DotYouContextAccessor _contextAccessor;
    private readonly FileSystemResolver _fileSystemResolver;

    public ReactionPreviewCalculator(DotYouContextAccessor contextAccessor, FileSystemResolver fileSystemResolver)
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

        await fs.Storage.UpdateStatistics(new InternalDriveFileId()
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

        targetFileReactionPreview.Comments.Add(new CommentPreview()
        {
            FileId = updatedFileHeader.FileMetadata.File.FileId,
            Created = updatedFileHeader.FileMetadata.Created,
            Updated = updatedFileHeader.FileMetadata.Updated,
            OdinId = _contextAccessor.GetCurrent().Caller.OdinId,
            JsonContent = updatedFileHeader.FileMetadata.AppData.JsonContent,
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

        var key = HashUtil.ReduceSHA256Hash(notification.Reaction.ReactionContent);
        if (!dict.TryGetValue(key, out ReactionContentPreview reactionPreview))
        {
            reactionPreview = new ReactionContentPreview();
        }

        reactionPreview.Count++;
        reactionPreview.ReactionContent = notification.Reaction.ReactionContent;
        reactionPreview.Key = key;

        dict[key] = reactionPreview;

        preview.Reactions = dict;

        fs.Storage.UpdateStatistics(targetFile, preview).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }
}