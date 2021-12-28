using System;
using Dawn;

namespace Youverse.Core.Services.Drive.Storage
{
    public class FileMetaData
    {
        public FileMetaData(DriveFileId file)
        {
            this.File = file;
            this.AppData = new AppFileMetaData();
        }
        
        public DriveFileId File { get; set; }
        public UInt64 Created { get; set; }
        public UInt64 Updated { get; set; }
        public string ContentType { get; set; }
        
        public AppFileMetaData AppData { get; set; }
    }
}