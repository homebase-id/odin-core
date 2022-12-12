using System;
using MediatR;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Mediator
{
    public class DriveDefinitionAddedNotification : EventArgs, INotification
    {
        public bool IsNewDrive { get; set; }
        public StorageDrive Drive { get; set; }
    }

    public class DriveFileChangedNotification : EventArgs, INotification
    {
        public InternalDriveFileId File { get; set; }

        public ServerFileHeader FileHeader { get; set; }
    }

    public class DriveFileDeletedNotification : EventArgs, INotification
    {
        public InternalDriveFileId File { get; set; }
    }
}