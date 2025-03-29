using System;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Authorization.Acl;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class ServerMetadata
    {
        public AccessControlList AccessControlList { get; set; }

        /// <summary>
        /// If true, the file should not be indexed
        /// </summary>
        [Obsolete]
        public bool DoNotIndex { get; set; }

        /// <summary>
        /// Indicates if this file can be distributed to Data Subscriptions
        /// </summary>
        public bool AllowDistribution { get; set; }

        /// <summary>
        /// Indicates the system type of file; this changes the internal behavior how the file is saved
        /// </summary>
        public FileSystemType FileSystemType { get; set; }

        public Int64 FileByteCount { get; set; }

        public int OriginalRecipientCount { get; set; }

        public RecipientTransferHistory TransferHistory { get; set; }

        public ServerMetadata()
        {
        }

        // The record is needed to fill in specific colums from the record that are not in the Dto,
        // i.e. the columns that are commented out above
        public ServerMetadata(ServerMetadataDto serverMetadataDto, DriveMainIndexRecord record)
        {
            AccessControlList = serverMetadataDto.AccessControlList;
            AllowDistribution = serverMetadataDto.AllowDistribution;
            // FileSystemType = serverMetadataDto.FileSystemType;
            FileByteCount = serverMetadataDto.FileByteCount;
            OriginalRecipientCount = serverMetadataDto.OriginalRecipientCount;

            TransferHistory = string.IsNullOrEmpty(record.hdrTransferHistory)
                ? null
                : OdinSystemSerializer.Deserialize<RecipientTransferHistory>(record.hdrTransferHistory);

            FileSystemType = (FileSystemType) record.fileSystemType;
        }
    }
}