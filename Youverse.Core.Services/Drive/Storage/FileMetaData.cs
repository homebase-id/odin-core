using System;

namespace Youverse.Core.Services.Drive.Storage
{
    public class FileMetaData
    {
        public UInt64 Created { get; set; }
        public UInt64 Updated { get; set; }
        public string FileType { get; set; }
        public AppFileMetaData AppData { get; set; }
    }
}