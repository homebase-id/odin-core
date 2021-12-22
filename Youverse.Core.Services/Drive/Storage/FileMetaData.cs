using System;

namespace Youverse.Core.Services.Drive.Storage
{
    public class FileMetaData
    {
        public FileMetaData()
        {
            this.AppData = new AppFileMetaData();
        }
        public UInt64 Created { get; set; }
        public UInt64 Updated { get; set; }
        public string ContentType { get; set; }
        
        public FileChecksum FileChecksum { get; set; }
        public AppFileMetaData AppData { get; set; }
    }
}