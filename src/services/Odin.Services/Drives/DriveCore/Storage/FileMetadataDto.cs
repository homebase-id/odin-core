using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class FileMetadataDto
    {
        public GlobalTransitIdFileIdentifier ReferencedFile { get; set; }

        public InternalDriveFileId File { get; set; }

        public Guid? GlobalTransitId { get; set; }

        public FileState FileState { get; set; }

        public Int64 Created { get; set; }

        public Int64 Updated { get; set; }

        public Int64 TransitCreated { get; set; }

        public Int64 TransitUpdated { get; set; }

        // public ReactionSummary ReactionPreview { get; set; }

        public bool IsEncrypted { get; set; }

        public string SenderOdinId { get; set; }

        public OdinId? OriginalAuthor { get; set; }

        // public AppFileMetaData AppData { get; set; }

        public LocalAppMetadata LocalAppData { get; set; }
        
        public List<PayloadDescriptor> Payloads { get; set; }

        // public Guid? VersionTag { get; set; }



        // The record is needed to fill in specific colums from the record that are not in the Dto,
        // i.e. the columns that are commented out above
        public FileMetadata ToFileMetadata(DriveMainIndexRecord record)
        {
            // TODO: Check if more colums should be commented out 

            var fileMetadata = new FileMetadata()
            {
                ReferencedFile = ReferencedFile,
                File = File,
                GlobalTransitId = GlobalTransitId,
                FileState = FileState,
                Created = Created, 
                Updated = Updated,
                TransitCreated = TransitCreated,
                TransitUpdated = TransitUpdated,
                // ReactionPreview = ReactionPreview,
                IsEncrypted = IsEncrypted,
                SenderOdinId = SenderOdinId,
                OriginalAuthor = OriginalAuthor,
                // AppData = AppData,
                LocalAppData = LocalAppData,
                Payloads = Payloads,
                // VersionTag = VersionTag,
            };

            // Now fill in FileMetadata with column specific values from the record
            // TODO: Add more records here, e.g. the FileId, GlobalTransitId, etc. all record.Fields
            // that are part of the FileMetadata
            //
            fileMetadata.VersionTag = record.hdrVersionTag;
            fileMetadata.AppData = OdinSystemSerializer.Deserialize<AppFileMetaData>(record.hdrAppData);
            fileMetadata.ReactionPreview = string.IsNullOrEmpty(record.hdrReactionSummary)
                ? null
                : OdinSystemSerializer.Deserialize<ReactionSummary>(record.hdrReactionSummary);

            fileMetadata.LocalAppData = string.IsNullOrEmpty(record.hdrLocalAppData)
                ? null
                : OdinSystemSerializer.Deserialize<LocalAppMetadata>(record.hdrLocalAppData);

            if (fileMetadata.LocalAppData != null)
            {
                fileMetadata.LocalAppData.VersionTag = record.hdrLocalVersionTag.GetValueOrDefault();
            }

            return fileMetadata;
        }
    }
}