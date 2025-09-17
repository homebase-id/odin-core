using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Peer.Encryption;
using System;
using Odin.Services.Base;

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
            header.ServerMetadata = new ServerMetadata(serverMetadataDto, record);

            // The database column is the master record. Function DriveMainIndex.UpdateByteCountAsync()
            // is called by the defragmenter to correct incorrect sizes. So that's the master value.
            header.ServerMetadata.FileByteCount = record.byteCount;

            // Set the FileMetadata
            var fileMetadataDto = OdinSystemSerializer.Deserialize<FileMetadataDto>(record.hdrFileMetaData);
            header.FileMetadata = new FileMetadata(fileMetadataDto, record);

            return header;
        }


        public DriveMainIndexRecord ToDriveMainIndexRecord(TargetDrive targetDrive, Guid identityId)
        {
            var fileMetadata = this.FileMetadata;
            var serverMetadata = this.ServerMetadata;
            var encryptedKeyHeader = this.EncryptedKeyHeader;
            int securityGroup = (int)serverMetadata.AccessControlList.RequiredSecurityGroup;

            var record = new DriveMainIndexRecord
            {
                identityId = identityId,
                driveId = fileMetadata.File.DriveId,
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
                created = fileMetadata.Created, // It will be ignored & overwritten by the DB layer
                modified = fileMetadata.Updated, // It will be ignored & overwritten by the DB layer

                // local data is updated by a specific method
                // hdrLocalVersionTag =  ...
                // hdrLocalAppData = ...

                //this is updated by the SaveReactionSummary method
                // hdrReactionSummary = OdinSystemSerializer.Serialize(header.FileMetadata.ReactionPreview),
                // this is handled by the SaveTransferHistory method
                // hdrTransferStatus = OdinSystemSerializer.Serialize(header.ServerMetadata.TransferHistory),

                // hdrTmpDriveAlias = drive.TargetDriveInfo.Alias,
                // hdrTmpDriveType = drive.TargetDriveInfo.Type

                hdrTmpDriveAlias = targetDrive.Alias,
                hdrTmpDriveType = targetDrive.Type
            };

            // The DTOs basically removes the fields (above) that are already in the 'record'
            // so that we don't save the same data twice
            record.hdrFileMetaData = OdinSystemSerializer.Serialize(new FileMetadataDto(this.FileMetadata));
            record.hdrServerData = OdinSystemSerializer.Serialize(new ServerMetadataDto(this.ServerMetadata));

            if (record.driveId == Guid.Empty || record.fileId == Guid.Empty)
            {
                throw new OdinSystemException("DriveId and FileId must be a non-empty GUID");
            }

            return record;
        }


        public bool TryValidate(IOdinContext odinContext)
        {
            try
            {
                Validate(odinContext);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Validate(IOdinContext odinContext)
        {
            FileMetadata?.Validate(odinContext.Tenant);
            if (FileMetadata != null && !FileMetadata.File.IsValid())
            {
                throw new OdinSystemException("FileMetadata.File is not valid");
            }

            ServerMetadata?.Validate();

            // TODO possibly validate the EncryptedKeyHeader here
        }
    }
}