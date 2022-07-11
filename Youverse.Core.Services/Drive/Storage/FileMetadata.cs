using System;
using System.Threading.Tasks;

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
            this.File = new InternalDriveFileId()
            {
                DriveId = Guid.Empty,
                FileId = Guid.Empty
            };

            this.AppData = new AppFileMetaData();
        }

        public FileMetadata(InternalDriveFileId file)
        {
            this.File = file;
            this.AppData = new AppFileMetaData();
        }

        public InternalDriveFileId File { get; set; }

        public UInt64 Created { get; set; }

        public UInt64 Updated { get; set; }
        
        /// <summary>
        /// Specifies the app which last updated this file
        /// </summary>
        //public Guid LastUpdatedAppId { get; set; }
        
        public string ContentType { get; set; }

        /// <summary>
        /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
        /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
        /// </summary>
        public bool PayloadIsEncrypted { get; set; }
        
        /// <summary>
        /// The DotYouId of the DI that sent this file.  If null, the file was uploaded by the owner.
        /// </summary>
        public string SenderDotYouId { get; set; }

        /// <summary>
        /// The size of the payload on disk
        /// </summary>
        public long PayloadSize { get; set; }

        public AppFileMetaData AppData { get; set; }
    }
}