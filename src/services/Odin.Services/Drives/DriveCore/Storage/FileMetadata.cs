using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Core.Util;

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
        public static readonly int MaxPayloadsCount = 25;

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

        public LocalAppMetadata LocalAppData { get; set; }
        
        public List<PayloadDescriptor> Payloads { get; set; }

        public Guid? VersionTag { get; set; }


        // The record is needed to fill in specific colums from the record that are not in the Dto,
        // i.e. the columns that are commented out above
        public FileMetadata(FileMetadataDto fileMetadataDto, DriveMainIndexRecord record)
        {
            // TODO: Check if more colums should be commented out 

            ReferencedFile = fileMetadataDto.ReferencedFile;
            File = fileMetadataDto.File;
            GlobalTransitId = fileMetadataDto.GlobalTransitId;
            FileState = fileMetadataDto.FileState;
            Created = fileMetadataDto.Created;
            Updated = fileMetadataDto.Updated;
            TransitCreated = fileMetadataDto.TransitCreated;
            TransitUpdated = fileMetadataDto.TransitUpdated;
            // ReactionPreview = ReactionPreview,
            IsEncrypted = fileMetadataDto.IsEncrypted;
            SenderOdinId = fileMetadataDto.SenderOdinId;
            OriginalAuthor = fileMetadataDto.OriginalAuthor;
            // AppData = AppData,
            LocalAppData = fileMetadataDto.LocalAppData;
            Payloads = fileMetadataDto.Payloads;
            // VersionTag = VersionTag,

            // Now fill in FileMetadata with column specific values from the record
            // TODO: Add more records here, e.g. the FileId, GlobalTransitId, etc. all record.Fields
            // that are part of the FileMetadata
            //
            VersionTag = record.hdrVersionTag;
            AppData = OdinSystemSerializer.Deserialize<AppFileMetaData>(record.hdrAppData);
            ReactionPreview = string.IsNullOrEmpty(record.hdrReactionSummary)
                ? null
                : OdinSystemSerializer.Deserialize<ReactionSummary>(record.hdrReactionSummary);

            LocalAppData = string.IsNullOrEmpty(record.hdrLocalAppData)
                ? null
                : OdinSystemSerializer.Deserialize<LocalAppMetadata>(record.hdrLocalAppData);

            if (LocalAppData != null)
            {
                LocalAppData.VersionTag = record.hdrLocalVersionTag.GetValueOrDefault();
            }
        }

        public PayloadDescriptor GetPayloadDescriptor(string key)
        {
            return Payloads?.SingleOrDefault(pk => string.Equals(pk.Key, key, StringComparison.InvariantCultureIgnoreCase));
        }

/*
        public FileMetadataDto ToFileMetadataDto()
        {
            var metadata = new FileMetadataDto()
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

            return metadata;
        }
*/
        public bool TryValidate()
        {
            try
            {
                Validate();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Validate()
        {
            ReactionPreview?.Validate();
            if (SenderOdinId != null)
                AsciiDomainNameValidator.AssertValidDomain(SenderOdinId); // Because senderOdinId is a string and not an OdinId...
            AppData?.Validate();
            LocalAppData?.Validate();

            if (Payloads != null)
            {
                if (Payloads?.Count > MaxPayloadsCount)
                    throw new OdinClientException($"Too many Payloads count {Payloads.Count} in FileMetadata max {MaxPayloadsCount}");

                foreach (var payload in Payloads)
                    payload.Validate();
            }
        }

    }
}