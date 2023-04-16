using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Drives.DriveCore.Storage
{
    public enum FileState
    {
        Active = 1,
        Deleted = 2,
        // Archived = 3
    }
    
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

        /// <summary>
        /// A file to which this file references.  I.e. this file is a comment about another file
        /// </summary>
        public GlobalTransitIdFileIdentifier ReferencedFile { get; set; }
        
        public InternalDriveFileId File { get; set; }
        
        /// <summary>
        /// A globally unique Id to cross reference this file across Identities 
        /// </summary>
        public Guid? GlobalTransitId { get; set; }
        
        public FileState FileState { get; set; }

        public Int64 Created { get; set; }

        public Int64 Updated { get; set; }
        
        public ReactionSummary ReactionPreview { get; set; }

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
        /// The OdinId of the DI that sent this file.  If null, the file was uploaded by the owner.
        /// </summary>
        public string SenderOdinId { get; set; }

        /// <summary>
        /// The size of the payload on disk
        /// </summary>
        public long PayloadSize { get; set; }
        
        /// <summary>
        /// Specifies the list of recipients set when the file was uploaded
        /// </summary>
        public List<string> OriginalRecipientList { get; set; }
        
        public AppFileMetaData AppData { get; set; }

        public Guid? ConcurrencyToken { get; set; }
    }
}