using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Core.Serialization;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;
using System;
using Org.BouncyCastle.Asn1.Crmf;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Services.Authorization.Acl;
using System.Text.RegularExpressions;
using System.Xml;
using System.Collections.Generic;

namespace Odin.Hosting.Tests.OwnerApi.Drive.CommentFileSystem
{
    public class DataMappingTest
    {
        [Test]
        public async Task DriveMainIndexRecordFillTest()
        {
            var uniqueId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var localTag = Guid.NewGuid();
            var userDate = new UnixTimeUtc(26);

            var driveMainRecord = new DriveMainIndexRecord()
            {
                archivalStatus = 7,
                byteCount = 42,
                created = new UnixTimeUtc(9),
                dataType = 10,
                driveId = Guid.NewGuid(),
                fileId = Guid.NewGuid(),
                fileState = 11,
                fileSystemType = 12,
                fileType = 13,
                globalTransitId = Guid.NewGuid(),
                groupId = groupId,
                hdrAppData = OdinSystemSerializer.Serialize(new AppFileMetaData()
                    { ArchivalStatus = 7, DataType = 10, FileType = 13, GroupId = groupId, UniqueId = uniqueId, UserDate = userDate }),
                hdrEncryptedKeyHeader = OdinSystemSerializer.Serialize(new EncryptedKeyHeader()
                {
                    Type = EncryptionType.Aes, EncryptionVersion = 1, EncryptedAesKey = Guid.NewGuid().ToByteArray(),
                    Iv = Guid.NewGuid().ToByteArray()
                }),
                hdrFileMetaData = OdinSystemSerializer.Serialize(new FileMetadataDto()
                {
                    IsEncrypted = true, OriginalAuthor = new OdinId("frodo.baggins.me"), Payloads = null, ReferencedFile = null,
                    TransitCreated = new UnixTimeUtc(7), TransitUpdated = new UnixTimeUtc(0)
                }),
                hdrLocalAppData = OdinSystemSerializer.Serialize(new LocalAppMetadata() { Content = "hello", VersionTag = localTag }),
                hdrLocalVersionTag = localTag,
                hdrReactionSummary = OdinSystemSerializer.Serialize(new ReactionSummary() { TotalCommentCount = 69 }),
                hdrServerData = OdinSystemSerializer.Serialize(new ServerMetadataDto()
                {
                    AccessControlList = new Services.Authorization.Acl.AccessControlList()
                        { RequiredSecurityGroup = SecurityGroupType.Anonymous },
                    AllowDistribution = false, FileByteCount = 42, OriginalRecipientCount = 69
                }),
                hdrTmpDriveAlias = Guid.NewGuid(),
                hdrTmpDriveType = Guid.NewGuid(),
                hdrTransferHistory = OdinSystemSerializer.Serialize(new TransferHistorySummary() { TotalDelivered = 7 }),
                hdrVersionTag = Guid.NewGuid(),
                historyStatus = 21,
                identityId = Guid.NewGuid(),
                modified = new UnixTimeUtc(22),
                requiredSecurityGroup = 111,
                rowId = 24,
                senderId = "frodo.25",
                uniqueId = uniqueId,
                userDate = userDate
            };

            var sfh = ServerFileHeader.FromDriveMainIndexRecord(driveMainRecord);

            // Ensure that the fields we only read in from the DB are in fact there
            ClassicAssert.IsTrue(sfh.FileMetadata.Created.milliseconds == 9);
            ClassicAssert.IsTrue(sfh.FileMetadata.Updated.milliseconds == 22);
            ClassicAssert.IsTrue(sfh.FileMetadata.LocalAppData != null);
            ClassicAssert.IsTrue(sfh.FileMetadata.LocalAppData.VersionTag != Guid.Empty);
            ClassicAssert.IsTrue(sfh.FileMetadata.ReactionPreview != null);
            ClassicAssert.IsTrue(sfh.ServerMetadata.TransferHistory != null);

            var targetDrive = new TargetDrive() { Alias = driveMainRecord.hdrTmpDriveAlias, Type = driveMainRecord.hdrTmpDriveType };
            var dmr2 = sfh.ToDriveMainIndexRecord(targetDrive);

            ClassicAssert.IsTrue(driveMainRecord.archivalStatus == dmr2.archivalStatus);
            ClassicAssert.IsTrue(driveMainRecord.byteCount == dmr2.byteCount);
            ClassicAssert.IsTrue(dmr2.created == driveMainRecord.created); // created doesn't get copied
            ClassicAssert.IsTrue(driveMainRecord.dataType == dmr2.dataType);
            ClassicAssert.IsTrue(driveMainRecord.driveId == dmr2.driveId);
            ClassicAssert.IsTrue(driveMainRecord.fileId == dmr2.fileId);
            ClassicAssert.IsTrue(driveMainRecord.fileState == dmr2.fileState);
            ClassicAssert.IsTrue(driveMainRecord.fileSystemType == dmr2.fileSystemType);
            ClassicAssert.IsTrue(driveMainRecord.fileType == dmr2.fileType);
            ClassicAssert.IsTrue(driveMainRecord.globalTransitId == dmr2.globalTransitId);
            ClassicAssert.IsTrue(driveMainRecord.groupId == dmr2.groupId);
            ClassicAssert.IsTrue(driveMainRecord.hdrAppData == dmr2.hdrAppData);
            ClassicAssert.IsTrue(driveMainRecord.hdrEncryptedKeyHeader == dmr2.hdrEncryptedKeyHeader);
            ClassicAssert.IsTrue(driveMainRecord.hdrFileMetaData == dmr2.hdrFileMetaData);
            ClassicAssert.IsTrue(driveMainRecord.hdrLocalAppData != null);
            ClassicAssert.IsTrue(dmr2.hdrLocalAppData == null); // We don't copy localAppData
            ClassicAssert.IsTrue(driveMainRecord.hdrLocalVersionTag != null);
            ClassicAssert.IsTrue(dmr2.hdrLocalVersionTag == null); // We don't copy localAppData.VersionTag
            ClassicAssert.IsTrue(driveMainRecord.hdrReactionSummary != null);
            ClassicAssert.IsTrue(dmr2.hdrReactionSummary == null); // We don't copy the reaction summary
            ClassicAssert.IsTrue(driveMainRecord.hdrServerData == dmr2.hdrServerData);
            ClassicAssert.IsTrue(driveMainRecord.hdrTmpDriveAlias == dmr2.hdrTmpDriveAlias);
            ClassicAssert.IsTrue(driveMainRecord.hdrTmpDriveType == dmr2.hdrTmpDriveType);
            ClassicAssert.IsTrue(driveMainRecord.hdrTransferHistory != null);
            ClassicAssert.IsTrue(dmr2.hdrTransferHistory == null); // We don't copy the transfer history
            ClassicAssert.IsTrue(driveMainRecord.hdrVersionTag == dmr2.hdrVersionTag);
            ClassicAssert.IsTrue(driveMainRecord.historyStatus == 21);
            ClassicAssert.IsTrue(dmr2.historyStatus == 0); // Hardcoded to 0
            ClassicAssert.IsTrue(driveMainRecord.identityId != Guid.Empty);
            ClassicAssert.IsTrue(dmr2.identityId == Guid.Empty); // Doesn't get copied back
            ClassicAssert.IsTrue(driveMainRecord.modified == dmr2.modified);
            ClassicAssert.IsTrue(driveMainRecord.requiredSecurityGroup == dmr2.requiredSecurityGroup);
            ClassicAssert.IsTrue(driveMainRecord.rowId != 0);
            ClassicAssert.IsTrue(dmr2.rowId == 0); // We don't copy in the rowId
            ClassicAssert.IsTrue(driveMainRecord.senderId == dmr2.senderId);
            ClassicAssert.IsTrue(driveMainRecord.uniqueId == dmr2.uniqueId);
            ClassicAssert.IsTrue(driveMainRecord.userDate == dmr2.userDate);

            await Task.Delay(0);
        }


        [Test]
        public async Task FileHeaderFillTest()
        {
            var uniqueId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var localTag = Guid.NewGuid();
            var userDate = new UnixTimeUtc(26);

            var targetDrive = new TargetDrive() { Alias = Guid.NewGuid(), Type = Guid.NewGuid() };

            var ehdr = new EncryptedKeyHeader()
            {
                Type = EncryptionType.Aes, EncryptionVersion = 1, EncryptedAesKey = Guid.NewGuid().ToByteArray(),
                Iv = Guid.NewGuid().ToByteArray()
            };

            var fhdr = new FileMetadata()
            {
                IsEncrypted = true,
                OriginalAuthor = new OdinId("frodo.baggins.me"),
                Payloads = new List<PayloadDescriptor>(),
                ReferencedFile = new GlobalTransitIdFileIdentifier() { GlobalTransitId = Guid.NewGuid(), TargetDrive = targetDrive },
                TransitCreated = new UnixTimeUtc(7),
                TransitUpdated = new UnixTimeUtc(0),
                AppData = new AppFileMetaData()
                {
                    ArchivalStatus = 7,
                    DataType = 10,
                    FileType = 13,
                    GroupId = groupId,
                    UniqueId = uniqueId,
                    UserDate = userDate
                },
                LocalAppData = new LocalAppMetadata() { Content = "hello", VersionTag = localTag },
                ReactionPreview = new ReactionSummary() { TotalCommentCount = 69 },
                VersionTag = Guid.NewGuid(),
                DataSubscriptionSource = new DataSubscriptionSource()
                {
                    Identity = (OdinId)"user.domain.com",
                    DriveId = targetDrive.Alias,
                    PayloadsAreRemote = true
                },
                Created = new UnixTimeUtc(9),
                Updated = new UnixTimeUtc(22),
                GlobalTransitId = Guid.NewGuid(),
                FileState = FileState.Active,
                SenderOdinId = "frodo.25",
                File = new InternalDriveFileId() { FileId = Guid.NewGuid(), DriveId = Guid.NewGuid() },
            };

            var shdr = new ServerMetadata()
            {
                AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Anonymous },
                AllowDistribution = false,
                FileByteCount = 42,
                OriginalRecipientCount = 69,
                TransferHistory = new RecipientTransferHistory() { Summary = new TransferHistorySummary() { TotalDelivered = 7 } },
                FileSystemType = FileSystemType.Standard,
            };

            var hdr = new ServerFileHeader()
            {
                EncryptedKeyHeader = ehdr,
                FileMetadata = fhdr,
                ServerMetadata = shdr
            };

            var dr = hdr.ToDriveMainIndexRecord(targetDrive);
            var sfh = ServerFileHeader.FromDriveMainIndexRecord(dr);

            // Ensure that the fields we only read in from the DB are in fact there
            ClassicAssert.IsTrue(sfh.FileMetadata.Created.milliseconds == hdr.FileMetadata.Created.milliseconds);
            ClassicAssert.IsTrue(sfh.FileMetadata.Updated.milliseconds == hdr.FileMetadata.Updated.milliseconds);

            // We don't transfer localAppData, Reactionpreview and transferhistory
            ClassicAssert.IsTrue(sfh.FileMetadata.LocalAppData == null);
            ClassicAssert.IsTrue(hdr.FileMetadata.LocalAppData != null);
            ClassicAssert.IsTrue(hdr.FileMetadata.LocalAppData.VersionTag != Guid.Empty);
            ClassicAssert.IsTrue(sfh.FileMetadata.ReactionPreview == null);
            ClassicAssert.IsTrue(hdr.FileMetadata.ReactionPreview != null);
            ClassicAssert.IsTrue(sfh.ServerMetadata.TransferHistory == null);
            ClassicAssert.IsTrue(hdr.ServerMetadata.TransferHistory != null);
            ClassicAssert.IsTrue(sfh.FileMetadata.AppData.ArchivalStatus == hdr.FileMetadata.AppData.ArchivalStatus);
            ClassicAssert.IsTrue(sfh.FileMetadata.AppData.DataType == hdr.FileMetadata.AppData.DataType);
            ClassicAssert.IsTrue(sfh.FileMetadata.AppData.FileType == hdr.FileMetadata.AppData.FileType);
            ClassicAssert.IsTrue(sfh.FileMetadata.AppData.GroupId == hdr.FileMetadata.AppData.GroupId);
            ClassicAssert.IsTrue(sfh.FileMetadata.AppData.UniqueId == hdr.FileMetadata.AppData.UniqueId);
            ClassicAssert.IsTrue(sfh.FileMetadata.AppData.UserDate == hdr.FileMetadata.AppData.UserDate);

            ClassicAssert.IsTrue(sfh.FileMetadata.OriginalAuthor == hdr.FileMetadata.OriginalAuthor);

            // New assertions adapted from DriveMainIndexRecordFillTest
            ClassicAssert.IsTrue(sfh.ServerMetadata.FileByteCount == hdr.ServerMetadata.FileByteCount); // byteCount
            ClassicAssert.IsTrue(sfh.FileMetadata.File.DriveId == hdr.FileMetadata.File.DriveId); // driveId
            ClassicAssert.IsTrue(sfh.FileMetadata.File.FileId == hdr.FileMetadata.File.FileId); // fileId
            ClassicAssert.IsTrue(sfh.FileMetadata.FileState == hdr.FileMetadata.FileState); // fileState
            ClassicAssert.IsTrue(sfh.ServerMetadata.FileSystemType == hdr.ServerMetadata.FileSystemType); // fileSystemType
            ClassicAssert.IsTrue(sfh.FileMetadata.GlobalTransitId == hdr.FileMetadata.GlobalTransitId); // globalTransitId

            // EncryptedKeyHeader fields (hdrEncryptedKeyHeader)
            ClassicAssert.IsTrue(sfh.EncryptedKeyHeader.Type == hdr.EncryptedKeyHeader.Type);
            ClassicAssert.IsTrue(sfh.EncryptedKeyHeader.EncryptionVersion == hdr.EncryptedKeyHeader.EncryptionVersion);
            ClassicAssert.AreEqual(sfh.EncryptedKeyHeader.EncryptedAesKey, hdr.EncryptedKeyHeader.EncryptedAesKey);
            ClassicAssert.AreEqual(sfh.EncryptedKeyHeader.Iv, hdr.EncryptedKeyHeader.Iv); // Same note as above

            // FileMetadata fields (hdrFileMetaData)
            ClassicAssert.IsTrue(sfh.FileMetadata.IsEncrypted == hdr.FileMetadata.IsEncrypted);
            ClassicAssert.IsTrue(sfh.FileMetadata.TransitCreated == hdr.FileMetadata.TransitCreated);
            ClassicAssert.IsTrue(sfh.FileMetadata.TransitUpdated == hdr.FileMetadata.TransitUpdated);
            ClassicAssert.IsTrue(sfh.FileMetadata.DataSubscriptionSource.Identity == hdr.FileMetadata.DataSubscriptionSource.Identity);
            ClassicAssert.IsTrue(sfh.FileMetadata.DataSubscriptionSource.DriveId == hdr.FileMetadata.DataSubscriptionSource.DriveId);
            ClassicAssert.IsTrue(sfh.FileMetadata.DataSubscriptionSource.PayloadsAreRemote == hdr.FileMetadata.DataSubscriptionSource.PayloadsAreRemote);

            ClassicAssert.AreEqual(sfh.FileMetadata.Payloads, hdr.FileMetadata.Payloads);
            ClassicAssert.AreEqual(sfh.FileMetadata.ReferencedFile, hdr.FileMetadata.ReferencedFile);

            // ServerMetadata fields (hdrServerData)
            ClassicAssert.IsTrue(sfh.ServerMetadata.AccessControlList.RequiredSecurityGroup ==
                                 hdr.ServerMetadata.AccessControlList.RequiredSecurityGroup); // requiredSecurityGroup
            ClassicAssert.IsTrue(sfh.ServerMetadata.AllowDistribution == hdr.ServerMetadata.AllowDistribution);
            ClassicAssert.IsTrue(sfh.ServerMetadata.OriginalRecipientCount == hdr.ServerMetadata.OriginalRecipientCount);

            // TargetDrive fields (hdrTmpDriveAlias, hdrTmpDriveType)
            // Note: These are not directly accessible in ServerFileHeader, but set via targetDrive in ToDriveMainIndexRecord
            // Assuming they map back correctly in the conversion, we can't check sfh directly, so skipping unless TargetDrive is exposed

            // VersionTag (hdrVersionTag)
            ClassicAssert.IsTrue(sfh.FileMetadata.VersionTag == hdr.FileMetadata.VersionTag);

            // SenderId
            ClassicAssert.IsTrue(sfh.FileMetadata.SenderOdinId == hdr.FileMetadata.SenderOdinId); // senderId
            await Task.Delay(0);
        }
    }
}