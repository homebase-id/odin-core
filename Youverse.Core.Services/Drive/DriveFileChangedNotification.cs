using System;
using MediatR;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Drive
{
    public class DriveFileChangedNotification : EventArgs, INotification
    {
        public DriveFileId File { get; set; }

        public FileMetadata FileMetadata { get; set; }
    }

    // public class DriveFileChangedHandler : INotificationHandler<DriveFileChangedArgs>
    // {
    //     
    //     public Task Handle(DriveFileChangedArgs notification, CancellationToken cancellationToken)
    //     {
    //         //update the index
    //     }
    // }
}