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
                hdrAppData = OdinSystemSerializer.Serialize(new AppFileMetaData() { ArchivalStatus = 7, DataType = 10, FileType = 13, GroupId = groupId, UniqueId = uniqueId, UserDate = userDate }),
                hdrEncryptedKeyHeader = OdinSystemSerializer.Serialize(new EncryptedKeyHeader() { Type = EncryptionType.Aes, EncryptionVersion = 1, EncryptedAesKey = Guid.NewGuid().ToByteArray(), Iv = Guid.NewGuid().ToByteArray() }),
                hdrFileMetaData = OdinSystemSerializer.Serialize(new FileMetadataDto() { IsEncrypted = true, OriginalAuthor = new OdinId("frodo.baggins.me"), Payloads = null, ReferencedFile = null, TransitCreated = new UnixTimeUtc(7), TransitUpdated = new UnixTimeUtc(0) }),
                hdrLocalAppData = OdinSystemSerializer.Serialize(new LocalAppMetadata() { Content = "hello", VersionTag = localTag }),
                hdrLocalVersionTag = localTag,
                hdrReactionSummary = OdinSystemSerializer.Serialize(new ReactionSummary() { TotalCommentCount = 69}),
                hdrServerData = OdinSystemSerializer.Serialize(new ServerMetadataDto() { AccessControlList = new Services.Authorization.Acl.AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Anonymous }, AllowDistribution = false, FileByteCount = 42, OriginalRecipientCount = 69 }),
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
            ClassicAssert.IsTrue(dmr2.created == UnixTimeUtc.ZeroTime); // created doesn't get copied
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
            ClassicAssert.IsTrue(driveMainRecord.modified != null);
            ClassicAssert.IsTrue(dmr2.modified == null);
            ClassicAssert.IsTrue(driveMainRecord.requiredSecurityGroup == dmr2.requiredSecurityGroup);
            ClassicAssert.IsTrue(driveMainRecord.rowId != 0);
            ClassicAssert.IsTrue(dmr2.rowId == 0); // We don't copy in the rowId
            ClassicAssert.IsTrue(driveMainRecord.senderId == dmr2.senderId);
            ClassicAssert.IsTrue(driveMainRecord.uniqueId == dmr2.uniqueId);
            ClassicAssert.IsTrue(driveMainRecord.userDate == dmr2.userDate);

            await Task.Delay(0);
        }
    }
}