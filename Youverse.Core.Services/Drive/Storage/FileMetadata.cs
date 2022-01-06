using System;
using Dawn;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive.Storage
{
    /// <summary>
    /// Metadata about the file being stored.  This data is managed by the system. See AppFileMetaData for
    /// data owned by the app
    /// </summary>
    public class FileMetadata
    {
        public FileMetadata()
        {
            this.File = new DriveFileId()
            {
                DriveId = Guid.Empty,
                FileId = Guid.Empty
            };
            
            this.AppData = new AppFileMetaData();
        }

        public FileMetadata(DriveFileId file)
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

    public class ServerMetadata
    {
        public ServerMetadata()
        {
        }
    }

    public class FileHeader
    {
        public EncryptedKeyHeader EncryptedKeyHeader { get; set; }
        public FileMetadata FileMetadata { get; set; }
        public ServerMetadata ServerMetadata { get; set; }
    }
}