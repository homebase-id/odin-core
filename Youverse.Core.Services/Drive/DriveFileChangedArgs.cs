using System;

namespace Youverse.Core.Services.Drive
{
    public class DriveFileChangedArgs : EventArgs
    {
        public DriveFileId File { get; set; }
    }
}