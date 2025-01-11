using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public enum FileState
    {
        Deleted = 0,
        Active = 1,
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
            this.Payloads = new List<PayloadDescriptor>();
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

        public Int64 TransitCreated { get; set; }

        public Int64 TransitUpdated { get; set; }

        public ReactionSummary ReactionPreview { get; set; }

        /// <summary>
        /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
        /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
        /// </summary>
        public bool IsEncrypted { get; set; }

        /// <summary>
        /// The OdinId of the DI that sent this file.  This might vary from the <see cref="OriginalAuthor"/>
        /// </summary>
        public string SenderOdinId { get; set; }

        /// <summary>
        /// The first identity which created this file.
        /// </summary>
        public OdinId? OriginalAuthor { get; set; }

        public AppFileMetaData AppData { get; set; }

        public LocalAppMetadataTag LocalAppData { get; set; }
        
        public List<PayloadDescriptor> Payloads { get; set; }

        public Guid? VersionTag { get; set; }

        public PayloadDescriptor GetPayloadDescriptor(string key)
        {
            return Payloads?.SingleOrDefault(pk => string.Equals(pk.Key, key, StringComparison.InvariantCultureIgnoreCase));
        }
    }
}