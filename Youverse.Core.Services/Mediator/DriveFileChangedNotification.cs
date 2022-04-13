using System;
using MediatR;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Mediator
{
    public class DriveFileChangedNotification : EventArgs, INotification
    {
        public InternalDriveFileId File { get; set; }

        public FileMetadata FileMetadata { get; set; }
    }

    public class DriveFileDeletedNotification : EventArgs, INotification
    {
        public InternalDriveFileId File { get; set; }
    }
}