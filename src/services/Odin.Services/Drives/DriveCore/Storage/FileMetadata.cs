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

        private UnixTimeUtc _created;
        public UnixTimeUtc Created { get => _created; init => _created = value; }

        private UnixTimeUtc _updated;
        public UnixTimeUtc Updated { get => _updated; init => _updated = value; }

        public UnixTimeUtc TransitCreated { get; set; }

        public UnixTimeUtc TransitUpdated { get; set; }

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


        public void SetCreatedModifiedWithDatabaseValue(UnixTimeUtc databaseCreated, UnixTimeUtc? databaseModified)
        {
            _created = databaseCreated;

            if (databaseModified != null)
                _updated = databaseModified.Value;
            else
                _updated = UnixTimeUtc.ZeroTime;
        }

        // The record is needed to fill in specific colums from the record that are not in the Dto,
        // i.e. the columns that are commented out above
        public FileMetadata(FileMetadataDto fileMetadataDto, DriveMainIndexRecord record)
        {
            // First fill in the data from the DTO object
            //
            ReferencedFile = fileMetadataDto.ReferencedFile;
            // File = fileMetadataDto.File;
            // GlobalTransitId = fileMetadataDto.GlobalTransitId;
            // FileState = fileMetadataDto.FileState;
            // Created = fileMetadataDto.Created;
            // Updated = fileMetadataDto.Updated;
            TransitCreated = fileMetadataDto.TransitCreated;
            TransitUpdated = fileMetadataDto.TransitUpdated;
            // ReactionPreview = ReactionPreview,
            IsEncrypted = fileMetadataDto.IsEncrypted;
            // SenderOdinId = fileMetadataDto.SenderOdinId;
            OriginalAuthor = fileMetadataDto.OriginalAuthor;
            // AppData = AppData,
            // LocalAppData = fileMetadataDto.LocalAppData;
            Payloads = fileMetadataDto.Payloads;
            // VersionTag = VersionTag,

            // SANITY CHECK:
            // There are SIX fields in the DTO.
            // There are SIXTEEN properties in the FileMetaData
            // There must be TEN assignments below

            // Now fill in FileMetadata with column specific values from the record
            //
            
            File = new InternalDriveFileId() { FileId = record.fileId, DriveId = record.driveId };
            GlobalTransitId = record.globalTransitId;
            FileState = (FileState)record.fileState;
            Created = record.created;
            Updated = record.modified == null ? UnixTimeUtc.ZeroTime : record.modified.Value; // Todd says NULL means zero
            // But I would prefer if it was nullable - except of course if we change it so that it's always set

            ReactionPreview = string.IsNullOrEmpty(record.hdrReactionSummary)
                ? null
                : OdinSystemSerializer.Deserialize<ReactionSummary>(record.hdrReactionSummary);
            SenderOdinId = record.senderId;
            AppData = OdinSystemSerializer.Deserialize<AppFileMetaData>(record.hdrAppData);
            LocalAppData = string.IsNullOrEmpty(record.hdrLocalAppData)
                ? null
                : OdinSystemSerializer.Deserialize<LocalAppMetadata>(record.hdrLocalAppData);

            if (LocalAppData != null)
            {
                LocalAppData.VersionTag = record.hdrLocalVersionTag.GetValueOrDefault();
            }

            VersionTag = record.hdrVersionTag;
        }

        public PayloadDescriptor GetPayloadDescriptor(string key)
        {
            return Payloads?.SingleOrDefault(pk => string.Equals(pk.Key, key, StringComparison.InvariantCultureIgnoreCase));
        }

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