using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Peer.Encryption;
using System;
using System.Collections.Generic;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public class ServerFileHeader
    {
        public EncryptedKeyHeader EncryptedKeyHeader { get; set; }

        public FileMetadata FileMetadata { get; set; }

        public ServerMetadata ServerMetadata { get; set; }

        public bool IsValid()
        {
            return this.EncryptedKeyHeader != null
                   && this.FileMetadata != null
                   && this.ServerMetadata != null;
        }

        public static ServerFileHeader FromDriveMainIndexRecord(DriveMainIndexRecord record, List<Guid> localTags)
        {
            if (null == record)
            {
                return null;
            }

            var header = new ServerFileHeader
            {
                EncryptedKeyHeader = OdinSystemSerializer.Deserialize<EncryptedKeyHeader>(record.hdrEncryptedKeyHeader),
                FileMetadata = OdinSystemSerializer.Deserialize<FileMetadata>(record.hdrFileMetaData),
                ServerMetadata = OdinSystemSerializer.Deserialize<ServerMetadata>(record.hdrServerData)
            };

            //Now overwrite with column specific values
            header.FileMetadata.VersionTag = record.hdrVersionTag;
            header.FileMetadata.AppData = OdinSystemSerializer.Deserialize<AppFileMetaData>(record.hdrAppData);
            header.FileMetadata.ReactionPreview = string.IsNullOrEmpty(record.hdrReactionSummary)
                ? null
                : OdinSystemSerializer.Deserialize<ReactionSummary>(record.hdrReactionSummary);
            header.ServerMetadata.TransferHistory = string.IsNullOrEmpty(record.hdrTransferHistory)
                ? null
                : OdinSystemSerializer.Deserialize<RecipientTransferHistory>(record.hdrTransferHistory);

            header.FileMetadata.LocalAppData = new LocalAppMetadata
            {
                VersionTag = record.hdrLocalVersionTag.GetValueOrDefault(),
                Content = record.hdrLocalAppData,
                Tags = localTags ?? []
            };

            return header;
        }
    }
}