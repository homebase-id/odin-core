using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public record FileMetadataDto
    {
        public GlobalTransitIdFileIdentifier ReferencedFile { get; init; }

        // public InternalDriveFileId File { get; init; }

        // public Guid? GlobalTransitId { get; init; }

        // public FileState FileState { get; init; }

        // public Int64 Created { get; init; }

        // public Int64 Updated { get; init; }

        public UnixTimeUtc TransitCreated { get; init; }

        public UnixTimeUtc TransitUpdated { get; init; }

        // public ReactionSummary ReactionPreview { get; init; }

        public bool IsEncrypted { get; init; }

        // public string SenderOdinId { get; init; }

        public OdinId? OriginalAuthor { get; init; }

        // public AppFileMetaData AppData { get; init; }

        // public LocalAppMetadata LocalAppData { get; init; }
        
        public List<PayloadDescriptor> Payloads { get; init; }

        // public Guid? VersionTag { get; init; }
        
        public RemotePayloadSource RemotePayloadSource { get; set; }

        public FileMetadataDto() { }

        public FileMetadataDto(FileMetadata fileMetadata)
        {
            ReferencedFile = fileMetadata.ReferencedFile;
            // File = fileMetadata.File;
            // GlobalTransitId = fileMetadata.GlobalTransitId;
            // FileState = fileMetadata.FileState;
            // Created = fileMetadata.Created;
            // Updated = fileMetadata.Updated;
            TransitCreated = fileMetadata.TransitCreated;
            TransitUpdated = fileMetadata.TransitUpdated;
            // ReactionPreview = ReactionPreview
            IsEncrypted = fileMetadata.IsEncrypted;
            // SenderOdinId = fileMetadata.SenderOdinId;
            OriginalAuthor = fileMetadata.OriginalAuthor;
            // AppData = fileMetadata.AppData
            // LocalAppData = fileMetadata.LocalAppData; <--- TODO TODD & MICHAEL SANITY HERE?! XXX
            Payloads = fileMetadata.Payloads;
            
            RemotePayloadSource = fileMetadata.RemotePayloadSource;
            // VersionTag = fileMetadata.VersionTag
        }

    }
}