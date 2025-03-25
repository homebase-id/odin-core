using System;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Authorization.Acl;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class ServerMetadataDto
    {
        public AccessControlList AccessControlList { get; set; }

        // public bool DoNotIndex { get; set; } OBSOLETE

        public bool AllowDistribution { get; set; }

        public FileSystemType FileSystemType { get; set; }

        public Int64 FileByteCount { get; set; }

        public int OriginalRecipientCount { get; set; }

        // public RecipientTransferHistory TransferHistory { get; set; } <-- SAVED SEPARATELY


        // The record is needed to fill in specific colums from the record that are not in the Dto,
        // i.e. the columns that are commented out above
        public ServerMetadata ToServerMetadata(DriveMainIndexRecord record)
        {
            // TODO: Check if more colums should be commented out 
            var serverMetadata = new ServerMetadata()
            {
                AccessControlList = AccessControlList,
                AllowDistribution = AllowDistribution,
                FileSystemType = FileSystemType,
                FileByteCount = FileByteCount,
                OriginalRecipientCount = OriginalRecipientCount
            };

            serverMetadata.TransferHistory = string.IsNullOrEmpty(record.hdrTransferHistory)
                ? null
                : OdinSystemSerializer.Deserialize<RecipientTransferHistory>(record.hdrTransferHistory);

            return serverMetadata;
        }
    }
}