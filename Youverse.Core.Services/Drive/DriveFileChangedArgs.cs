using System;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Drive
{
    public class DriveFileChangedArgs : EventArgs
    {
        public DriveFileId File { get; set; }

        public FileMetadata FileMetadata { get; set; }
    }
}