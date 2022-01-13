using System;
using MediatR;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Mediator
{
    public class DriveFileChangedNotification : EventArgs, INotification
    {
        public DriveFileId File { get; set; }

        public FileMetadata FileMetadata { get; set; }
    }
}