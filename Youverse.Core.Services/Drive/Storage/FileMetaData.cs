using System;
using Dawn;

namespace Youverse.Core.Services.Drive.Storage
{
    /// <summary>
    /// Metadata about the file being stored.  This data is managed by the system. See AppFileMetaData for
    /// data owned by the app
    /// </summary>
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