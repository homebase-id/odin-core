using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Drives.Statistics;

/// <summary>
/// Listens for reaction file additions/changes and updates their target's preview
/// </summary>
public class ReactionPreviewCalculator : INotificationHandler<IDriveNotification>, INotificationHandler<EmojiReactionAddedNotification>
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
        if (updatedFileHeader.FileMetadata.ReferencedFile == null)
        {
            return;
        }

        if (notification.DriveNotificationType == DriveNotificationType.FileAdded)
        {
            var referenceFileDriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(updatedFileHeader.FileMetadata.ReferencedFile!.TargetDrive);
            var targetFile = new InternalDriveFileId()
            {
                DriveId = referenceFileDriveId,
                FileId = updatedFileHeader.FileMetadata.ReferencedFile.FileId
            };

            var fs = _fileSystemResolver.ResolveFileSystem(targetFile);
            var targetFileHeader = await fs.Storage.GetServerFileHeader(targetFile);
            var reactionPreview = targetFileHeader.ReactionPreview ?? new ReactionPreviewData();

            // await HandleFileAdded();

            //Always increment even if we don't store the contents
            reactionPreview.TotalCommentCount++;

            if (reactionPreview.Comments.Count > 3) //TODO: add to config
            {
                return;
            }

            reactionPreview.Comments.Add(new CommentPreview()
            {
                Created = updatedFileHeader.FileMetadata.Created,
                Updated = updatedFileHeader.FileMetadata.Updated,
                OdinId = _contextAccessor.GetCurrent().Caller.OdinId,
                JsonContent = updatedFileHeader.FileMetadata.AppData.JsonContent,
                Reactions = new List<EmojiReactionPreview>()
            });

            await fs.Storage.UpdateStatistics(targetFile, reactionPreview);
        }
    }

    public Task Handle(EmojiReactionAddedNotification notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.Reaction.FileId;
        var fs = _fileSystemResolver.ResolveFileSystem(targetFile);
        var header = fs.Storage.GetServerFileHeader(targetFile).GetAwaiter().GetResult();
        var preview = header.ReactionPreview ?? new ReactionPreviewData();

        var dict = preview.Reactions ?? new Dictionary<Guid, EmojiReactionPreview>();

        var key = HashUtil.ReduceSHA256Hash(notification.Reaction.ReactionContent);
        if (!dict.TryGetValue(key, out EmojiReactionPreview emojiPreview))
        {
            emojiPreview = new EmojiReactionPreview();
        }

        emojiPreview.Count++;
        emojiPreview.ReactionContent = notification.Reaction.ReactionContent;
        emojiPreview.Key = key;

        dict[key] = emojiPreview;

        preview.Reactions = dict;

        fs.Storage.UpdateStatistics(targetFile, preview).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }
}