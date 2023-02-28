using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.FileSystem;
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
        var updatedFileHeader = notification.ServerFileHeader;
        if (updatedFileHeader.FileMetadata.ReferencedFile == null)
        {
            return;
        }

        var referenceFileDriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(updatedFileHeader.FileMetadata.ReferencedFile!.TargetDrive);
        var targetFile = new InternalDriveFileId()
        {
            DriveId = referenceFileDriveId,
            FileId = updatedFileHeader.FileMetadata.ReferencedFile.FileId
        };

        var fs = _fileSystemResolver.ResolveFileSystem(targetFile);
        var targetFileHeader = await fs.Storage.GetServerFileHeader(targetFile);
        var reactionPreview = targetFileHeader.ReactionPreview ?? new ReactionPreviewData();

        //TODO: handle when the reference file is deleted therefore I need a way to determine all files that reference this one.
        //TODO: handle all variations
        // switch (notification.NotificationType)
        // {
        //     case ClientNotificationType.FileAdded:
        //         break;
        //     case ClientNotificationType.FileDeleted:
        //         break;
        //     case ClientNotificationType.FileModified:
        //         break;
        //     case ClientNotificationType.StatisticsChanged:
        //         break;
        //     default:
        //         throw new ArgumentOutOfRangeException();
        // }
        //

        //TODO: handle encrypted content?

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


    public Task Handle(EmojiReactionAddedNotification notification, CancellationToken cancellationToken)
    {
        var targetFile = notification.Reaction.FileId;
        var fs = _fileSystemResolver.ResolveFileSystem(targetFile);
        var header = fs.Storage.GetServerFileHeader(targetFile).GetAwaiter().GetResult();
        var preview = header.ReactionPreview ?? new ReactionPreviewData();
        
        var dict = preview.Reactions2;
        
        var key = HashUtil.ReduceSHA256Hash(notification.Reaction.ReactionContent);
        if (!dict.TryGetValue(key, out EmojiReactionPreview emojiPreview))
        {
            emojiPreview = new EmojiReactionPreview();
        }
        
        emojiPreview.Count++;
        emojiPreview.ReactionContent = notification.Reaction.ReactionContent;
        emojiPreview.Key = key;

        dict[key] = emojiPreview;

        preview.Reactions2 = dict;
        preview.Reactions = dict.Values.ToList();
        
        fs.Storage.UpdateStatistics(targetFile, preview).GetAwaiter().GetResult();
        return Task.CompletedTask;
        
    }
}