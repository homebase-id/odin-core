using System;
using Youverse.Core.Services.Authorization.Acl;

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
        public string ContentType { get; set; }


        /// <summary>
        /// The DotYouId of the DI that sent this file.  If null, the file was uploaded by the owner.
        /// </summary>
        public string SenderDotYouId { get; set; }

        public AccessControlList AccessControlList { get; set; }
        
        public AppFileMetaData AppData { get; set; }
    }
}