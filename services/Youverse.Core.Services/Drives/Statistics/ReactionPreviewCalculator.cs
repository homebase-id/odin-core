using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Mediator;

namespace Youverse.Core.Services.Drives.Statistics;

/// <summary>
/// Listens for reaction file additions/changes and updates their target's preview
/// </summary>
public class ReactionPreviewCalculator : INotificationHandler<IDriveNotification>
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
        var serverFileHeader = notification.ServerFileHeader;

        if (serverFileHeader.FileMetadata.ReferencedFile == null)
        {
            return;
        }


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
        
        //TODO: handle when the reference file is deleted
        // therefore I need a way to determine all files that reference this one.

        var fs = _fileSystemResolver.ResolveFileSystem(serverFileHeader.ServerMetadata.FileSystemType);

        var referenceFileDriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(serverFileHeader.FileMetadata.ReferencedFile!.TargetDrive);
        var file = new InternalDriveFileId()
        {
            DriveId = referenceFileDriveId,
            FileId = serverFileHeader.FileMetadata.ReferencedFile.FileId
        };

        var reactionPreview = serverFileHeader.ReactionPreview ?? new ReactionPreviewData();

        //TODO: handle encrypted content?

        if (reactionPreview.Comments.Count > 3)
        {
            return;
        }

        //TODO: Build in limits, etc.


        reactionPreview.Comments.Add(new CommentPreview()
        {
            DotYouId = _contextAccessor.GetCurrent().Caller.DotYouId,
            JsonContent = serverFileHeader.FileMetadata.AppData.JsonContent,
            Reactions = new List<EmojiReactionPreview>()
        });

        await fs.Storage.UpdateStatistics(file, reactionPreview);
    }
}