using System;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Authorization.Acl;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public record ServerMetadataDto
    {
        public AccessControlList AccessControlList { get; init; }

        // public bool DoNotIndex { get; init; } OBSOLETE

        public bool AllowDistribution { get; init; }

        // public FileSystemType FileSystemType { get; init; }

        public Int64 FileByteCount { get; init; }

        public int OriginalRecipientCount { get; init; }

        // public RecipientTransferHistory TransferHistory { get; init; } <-- SAVED SEPARATELY

        public ServerMetadataDto() { }

        public ServerMetadataDto(ServerMetadata serverMetadata)
        {
            AccessControlList = serverMetadata.AccessControlList;
            // FileSystemType = serverMetadata.FileSystemType;
            FileByteCount = serverMetadata.FileByteCount;
            OriginalRecipientCount = serverMetadata.OriginalRecipientCount;
            AllowDistribution = serverMetadata.AllowDistribution;
        }
    }
}