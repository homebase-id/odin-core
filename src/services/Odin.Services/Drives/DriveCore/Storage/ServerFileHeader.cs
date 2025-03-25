﻿using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Peer.Encryption;
using System;

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

        public static ServerFileHeader FromDriveMainIndexRecord(DriveMainIndexRecord record)
        {
            if (null == record)
            {
                return null;
            }

            var header = new ServerFileHeader
            {
                EncryptedKeyHeader = OdinSystemSerializer.Deserialize<EncryptedKeyHeader>(record.hdrEncryptedKeyHeader)
            };

            // Set the ServerMetadata
            var serverMetadataDto = OdinSystemSerializer.Deserialize<ServerMetadataDto>(record.hdrServerData);
            header.ServerMetadata = serverMetadataDto.ToServerMetadata(record);

            // Set the FileMetadata
            var fileMetadataDto = OdinSystemSerializer.Deserialize<FileMetadataDto>(record.hdrFileMetaData);
            header.FileMetadata = fileMetadataDto.ToFileMetadata(record);

            return header;
        }


        public DriveMainIndexRecord ToDriveMainIndexRecord(StorageDrive drive)
        {
            var fileMetadata = this.FileMetadata;
            var serverMetadata = this.ServerMetadata;
            var encryptedKeyHeader = this.EncryptedKeyHeader;
            int securityGroup = (int)serverMetadata.AccessControlList.RequiredSecurityGroup;

            var record = new DriveMainIndexRecord
            {
                identityId = default, // Assuming default is appropriate; clarify if needed
                driveId = drive.Id,
                fileId = fileMetadata.File.FileId,
                globalTransitId = fileMetadata.GlobalTransitId,
                uniqueId = fileMetadata.AppData.UniqueId,
                groupId = fileMetadata.AppData.GroupId,
                senderId = fileMetadata.SenderOdinId,
                fileType = fileMetadata.AppData.FileType,
                dataType = fileMetadata.AppData.DataType,
                archivalStatus = fileMetadata.AppData.ArchivalStatus,
                historyStatus = 0, // Hardcoded as in original code
                userDate = fileMetadata.AppData.UserDate ?? UnixTimeUtc.ZeroTime,
                requiredSecurityGroup = securityGroup,
                fileState = (int)fileMetadata.FileState,
                fileSystemType = (int)serverMetadata.FileSystemType,
                byteCount = serverMetadata.FileByteCount,
                hdrEncryptedKeyHeader = OdinSystemSerializer.Serialize(this.EncryptedKeyHeader),
                hdrVersionTag = fileMetadata.VersionTag.GetValueOrDefault(),
                hdrAppData = OdinSystemSerializer.Serialize(fileMetadata.AppData),

                // local data is updated by a specific method
                // hdrLocalVersionTag =  ...
                // hdrLocalAppData = ...

                //this is updated by the SaveReactionSummary method
                // hdrReactionSummary = OdinSystemSerializer.Serialize(header.FileMetadata.ReactionPreview),
                // this is handled by the SaveTransferHistory method
                // hdrTransferStatus = OdinSystemSerializer.Serialize(header.ServerMetadata.TransferHistory),

                hdrTmpDriveAlias = drive.TargetDriveInfo.Alias,
                hdrTmpDriveType = drive.TargetDriveInfo.Type                // Populate fields using drive, metadata, serverMetadata, encryptedKeyHeader
            };

            record.hdrFileMetaData = OdinSystemSerializer.Serialize(this.FileMetadata.ToFileMetadataDto());
            record.hdrServerData = OdinSystemSerializer.Serialize(this.ServerMetadata.ToServerMetadataDto());

            if (record.driveId == Guid.Empty || record.fileId == Guid.Empty)
            {
                throw new OdinSystemException("DriveId and FileId must be a non-empty GUID");
            }

            return record;
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
            FileMetadata?.Validate();

            // TODO possibly validate the ServerMetadata and EncryptedKeyHeader here
        }
    }
}