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

namespace Odin.Hosting.Tests.OwnerApi.Drive.CommentFileSystem
{
    public class DataMappingTest
    {
        [Test]
        public async Task DeserializationTest()
        {
            string fileMetadata =
                """
                {"referencedFile":null,"file":{"driveId":"a3b7c39f-10a1-4ce7-be15-d2a5f7e63235","fileId":"2b875c19-306d-e900-da21-477d35a2a2cb"},"globalTransitId":"6e5ff502-d7c7-44fd-b07d-5a73ff1e1135","fileState":"active","created":1742824716023,"updated":1742824716024,"transitCreated":0,"transitUpdated":0,"isEncrypted":true,"senderOdinId":"merry.dotyou.cloud","originalAuthor":"merry.dotyou.cloud","localAppData":null,"payloads":[{"iv":"v4nJUFUS97csusV4f6K+8w==","key":"test_key_1","contentType":"text/plain","bytesWritten":32,"lastModified":1742824716013,"descriptorContent":null,"previewThumbnail":{"content":"aVZCT1J3MEtHZ29BQUFBTlNVaEVVZ0FBQU9FQUFBRGhDQU1BQUFBSmJTSklBQUFBTTFCTVZFWC8vLy8vL3dBQUFBQ1dBQUQvc0xDZEFBRC9nSUQvdUxqL2hvYU1BQUQvczdQL2ZIeWtBQUQvd01EL2pJei91N3VDQUFDLzZmeVVBQUFCcDBsRVFWUjRuTzNheTFLRE1CaUEwVnFrRjYzVzkzOWFOeDA3MDBoTVFpQ3BuRzlMSWYrWkxESU0zZTBrU1pMeTI4K3I5ZmdKRVJMMkh5RmgveEUrbXpBYzhXVmUzWmtKQ1FrSkNRbTNKNnp1NmM1TVNFaElTRWk0WWVHYW5wVHFVd25YanBDUXNIMkVoSVR0SXlRa2JCOGg0WWFFSVd3ZlZHdldTQXRTQ1FrSkNRa0pDWC83U2hHNVZBWkxXVFN5QkNFaElTRWhJV0d4TUFKYlFWaTJSQjZWa0pDUWtKQ1FNRTBZL3JpNk1Hc0pRa0pDUWtKQ3dtSmhlSCtrTEdIV2t3a0pDUWtKQ1FuWEVmWWZJV0gvRVJMMkh5RmgrS0ExSXlRa0pGdytRa0xDUDU1NEdhdDBtVWxkVURnT1ZSb0pDUWtKQ1FrRFVOdnpNSVU2VDVpMFl5bkNRaGdoSVNFaElTRWhJU0VoSVNFaDRiTEM4NjJoRjJHRVdpUThmeHludWg1T0QzMnVBYXN1UEw1TzlmWitlT2hFU0VoSVNQaC9oRDlmdVdPeVcxOHB3bDdPdzV6TnZCLzBrOEM3TUxaMS9Rb2pNa0pDUWtMQ2JRbW4vK0EyVEwvNGhtL0FzWU4rUVdGSURlY0lMNVVWOFN3SUl5UWtKQ1FrSkN3MWw5WFlRMGhJU0VoSVNKaFdPR0pXcmNkUGlKQ3cvd2dKKzQvdytZV1NKRW1TSkVtU0pFbVNKS20wYjc4V3NjM3YzRjloQUFBQUFFbEZUa1N1UW1DQw==","pixelWidth":100,"pixelHeight":100,"contentType":"image/png","bytesWritten":0},"thumbnails":[{"pixelWidth":200,"pixelHeight":200,"contentType":"image/png","bytesWritten":6528}],"uid":114217760586989568},{"iv":"G0EUNmUZawN1NasdJJ1X1Q==","key":"test_key_2","contentType":"text/plain","bytesWritten":48,"lastModified":1742824716013,"descriptorContent":null,"previewThumbnail":{"content":"aVZCT1J3MEtHZ29BQUFBTlNVaEVVZ0FBQU9FQUFBRGhDQU1BQUFBSmJTSklBQUFBTTFCTVZFWC8vLy8vL3dBQUFBQ1dBQUQvc0xDZEFBRC9nSUQvdUxqL2hvYU1BQUQvczdQL2ZIeWtBQUQvd01EL2pJei91N3VDQUFDLzZmeVVBQUFCcDBsRVFWUjRuTzNheTFLRE1CaUEwVnFrRjYzVzkzOWFOeDA3MDBoTVFpQ3BuRzlMSWYrWkxESU0zZTBrU1pMeTI4K3I5ZmdKRVJMMkh5RmgveEUrbXpBYzhXVmUzWmtKQ1FrSkNRbTNKNnp1NmM1TVNFaElTRWk0WWVHYW5wVHFVd25YanBDUXNIMkVoSVR0SXlRa2JCOGg0WWFFSVd3ZlZHdldTQXRTQ1FrSkNRa0pDWC83U2hHNVZBWkxXVFN5QkNFaElTRWhJV0d4TUFKYlFWaTJSQjZWa0pDUWtKQ1FNRTBZL3JpNk1Hc0pRa0pDUWtKQ3dtSmhlSCtrTEdIV2t3a0pDUWtKQ1FuWEVmWWZJV0gvRVJMMkh5RmgrS0ExSXlRa0pGdytRa0xDUDU1NEdhdDBtVWxkVURnT1ZSb0pDUWtKQ1FrRFVOdnpNSVU2VDVpMFl5bkNRaGdoSVNFaElTRWhJU0VoSVNFaDRiTEM4NjJoRjJHRVdpUThmeHludWg1T0QzMnVBYXN1UEw1TzlmWitlT2hFU0VoSVNQaC9oRDlmdVdPeVcxOHB3bDdPdzV6TnZCLzBrOEM3TUxaMS9Rb2pNa0pDUWtMQ2JRbW4vK0EyVEwvNGhtL0FzWU4rUVdGSURlY0lMNVVWOFN3SUl5UWtKQ1FrSkN3MWw5WFlRMGhJU0VoSVNKaFdPR0pXcmNkUGlKQ3cvd2dKKzQvdytZV1NKRW1TSkVtU0pFbVNKS20wYjc4V3NjM3YzRjloQUFBQUFFbEZUa1N1UW1DQw==","pixelWidth":100,"pixelHeight":100,"contentType":"image/png","bytesWritten":0},"thumbnails":[{"pixelWidth":400,"pixelHeight":400,"contentType":"image/png","bytesWritten":20352}],"uid":114217760586989569}]}
                """;

            string serverMetadata =
                """
                {"accessControlList":{"requiredSecurityGroup":"connected","circleIdList":["42278063-3a2c-403c-b579-cade72de0d0f"],"odinIdList":null},"allowDistribution":true,"fileSystemType":"standard","fileByteCount":30850,"originalRecipientCount":1}
                """;

            var header = new ServerFileHeader
            {
                // EncryptedKeyHeader = OdinSystemSerializer.Deserialize<EncryptedKeyHeader>(record.hdrEncryptedKeyHeader),
                FileMetadata = OdinSystemSerializer.Deserialize<FileMetadata>(fileMetadata),
                ServerMetadata = OdinSystemSerializer.Deserialize<ServerMetadata>(serverMetadata)
            };

            // Check FileMetadata is still working
            header.FileMetadata.AppData = new AppFileMetaData() {Content = ":-)" };
            header.FileMetadata.ReactionPreview = new ReactionSummary() { TotalCommentCount = 7 };
            header.FileMetadata.VersionTag = Guid.NewGuid();
            var fileMetadata2 = OdinSystemSerializer.Serialize(header.FileMetadata.ToFileMetadataDto());
            ClassicAssert.IsTrue(fileMetadata2.Trim() == fileMetadata.Trim());

            // Check ServerMetadata is still working
            header.ServerMetadata.TransferHistory = new RecipientTransferHistory();
            var serverMetadata2 = OdinSystemSerializer.Serialize(header.ServerMetadata.ToServerMetadataDto());
            ClassicAssert.IsTrue(serverMetadata.Trim() == serverMetadata2.Trim());

            await Task.Delay(0);
        }

        // Fails on purpose right now
        [Test]
        public async Task RoundTripTest()
        {
            string driveMainRecordStr =
                """
                {"rowId":0,"identityId":"00000000-0000-0000-0000-000000000000","driveId":"edee9397-a3d4-4981-b2cc-cdc685144b7b","fileId":"4b3a5d19-50a7-3f00-2118-c63f69b54765","globalTransitId":"3909cd7f-8ef2-46a1-9ba8-35cf4ec9ceb5","fileState":1,"requiredSecurityGroup":777,"fileSystemType":128,"userDate":0,"fileType":100,"dataType":7779,"archivalStatus":0,"historyStatus":0,"senderId":"merry.dotyou.cloud","groupId":null,"uniqueId":null,"byteCount":30855,"hdrEncryptedKeyHeader":"{\u0022encryptionVersion\u0022:1,\u0022type\u0022:\u0022aes\u0022,\u0022iv\u0022:\u0022p2T6n7/I2nLd9QgNY9KKFg==\u0022,\u0022encryptedAesKey\u0022:\u00226iKPRIxhVUHD47s5KbxV7ZonKAvr\u002BElI0QXnc6cMz9T5DemVNA2jHdTfscslIiRL\u0022}","hdrVersionTag":"4b3a5d19-c0a9-ff00-edef-70799b813b37","hdrAppData":"{\u0022uniqueId\u0022:null,\u0022tags\u0022:null,\u0022fileType\u0022:100,\u0022dataType\u0022:7779,\u0022groupId\u0022:null,\u0022userDate\u0022:null,\u0022content\u0022:\u0022GRGX9t3ZClYxNZO9EhNNgMHh21a/NFK5wB9K\\u002BxfnGZs=\u0022,\u0022previewThumbnail\u0022:null,\u0022archivalStatus\u0022:0}","hdrLocalVersionTag":null,"hdrLocalAppData":"{\u0022SomeRandomData\u0022:42}","hdrReactionSummary":null,"hdrServerData":"{\u0022accessControlList\u0022:{\u0022requiredSecurityGroup\u0022:\u0022connected\u0022,\u0022circleIdList\u0022:[\u002290f3f364-2eac-48bd-ad9c-86a0a91f671f\u0022],\u0022odinIdList\u0022:null},\u0022allowDistribution\u0022:true,\u0022fileSystemType\u0022:\u0022standard\u0022,\u0022fileByteCount\u0022:30855,\u0022originalRecipientCount\u0022:1}","hdrTransferHistory":null,"hdrFileMetaData":"{\u0022referencedFile\u0022:null,\u0022file\u0022:{\u0022driveId\u0022:\u0022edee9397-a3d4-4981-b2cc-cdc685144b7b\u0022,\u0022fileId\u0022:\u00224b3a5d19-50a7-3f00-2118-c63f69b54765\u0022},\u0022globalTransitId\u0022:\u00223909cd7f-8ef2-46a1-9ba8-35cf4ec9ceb5\u0022,\u0022fileState\u0022:\u0022active\u0022,\u0022created\u0022:1743012543132,\u0022updated\u0022:1743012543132,\u0022transitCreated\u0022:0,\u0022transitUpdated\u0022:0,\u0022isEncrypted\u0022:true,\u0022senderOdinId\u0022:\u0022merry.dotyou.cloud\u0022,\u0022originalAuthor\u0022:\u0022merry.dotyou.cloud\u0022,\u0022localAppData\u0022:null,\u0022payloads\u0022:[{\u0022iv\u0022:\u0022zGEZK8LVn1nU1SDQsnh7SA==\u0022,\u0022key\u0022:\u0022test_key_1\u0022,\u0022contentType\u0022:\u0022text/plain\u0022,\u0022bytesWritten\u0022:32,\u0022lastModified\u0022:1743012543121,\u0022descriptorContent\u0022:null,\u0022previewThumbnail\u0022:{\u0022content\u0022:\u0022aVZCT1J3MEtHZ29BQUFBTlNVaEVVZ0FBQU9FQUFBRGhDQU1BQUFBSmJTSklBQUFBTTFCTVZFWC8vLy8vL3dBQUFBQ1dBQUQvc0xDZEFBRC9nSUQvdUxqL2hvYU1BQUQvczdQL2ZIeWtBQUQvd01EL2pJei91N3VDQUFDLzZmeVVBQUFCcDBsRVFWUjRuTzNheTFLRE1CaUEwVnFrRjYzVzkzOWFOeDA3MDBoTVFpQ3BuRzlMSWYrWkxESU0zZTBrU1pMeTI4K3I5ZmdKRVJMMkh5RmgveEUrbXpBYzhXVmUzWmtKQ1FrSkNRbTNKNnp1NmM1TVNFaElTRWk0WWVHYW5wVHFVd25YanBDUXNIMkVoSVR0SXlRa2JCOGg0WWFFSVd3ZlZHdldTQXRTQ1FrSkNRa0pDWC83U2hHNVZBWkxXVFN5QkNFaElTRWhJV0d4TUFKYlFWaTJSQjZWa0pDUWtKQ1FNRTBZL3JpNk1Hc0pRa0pDUWtKQ3dtSmhlSCtrTEdIV2t3a0pDUWtKQ1FuWEVmWWZJV0gvRVJMMkh5RmgrS0ExSXlRa0pGdytRa0xDUDU1NEdhdDBtVWxkVURnT1ZSb0pDUWtKQ1FrRFVOdnpNSVU2VDVpMFl5bkNRaGdoSVNFaElTRWhJU0VoSVNFaDRiTEM4NjJoRjJHRVdpUThmeHludWg1T0QzMnVBYXN1UEw1TzlmWitlT2hFU0VoSVNQaC9oRDlmdVdPeVcxOHB3bDdPdzV6TnZCLzBrOEM3TUxaMS9Rb2pNa0pDUWtMQ2JRbW4vK0EyVEwvNGhtL0FzWU4rUVdGSURlY0lMNVVWOFN3SUl5UWtKQ1FrSkN3MWw5WFlRMGhJU0VoSVNKaFdPR0pXcmNkUGlKQ3cvd2dKKzQvdytZV1NKRW1TSkVtU0pFbVNKS20wYjc4V3NjM3YzRjloQUFBQUFFbEZUa1N1UW1DQw==\u0022,\u0022pixelWidth\u0022:100,\u0022pixelHeight\u0022:100,\u0022contentType\u0022:\u0022image/png\u0022,\u0022bytesWritten\u0022:0},\u0022thumbnails\u0022:[{\u0022pixelWidth\u0022:200,\u0022pixelHeight\u0022:200,\u0022contentType\u0022:\u0022image/png\u0022,\u0022bytesWritten\u0022:6528}],\u0022uid\u0022:114230070024142848},{\u0022iv\u0022:\u0022KxYO2O8BMGBE8j2mo6MF3w==\u0022,\u0022key\u0022:\u0022test_key_2\u0022,\u0022contentType\u0022:\u0022text/plain\u0022,\u0022bytesWritten\u0022:48,\u0022lastModified\u0022:1743012543121,\u0022descriptorContent\u0022:null,\u0022previewThumbnail\u0022:{\u0022content\u0022:\u0022aVZCT1J3MEtHZ29BQUFBTlNVaEVVZ0FBQU9FQUFBRGhDQU1BQUFBSmJTSklBQUFBTTFCTVZFWC8vLy8vL3dBQUFBQ1dBQUQvc0xDZEFBRC9nSUQvdUxqL2hvYU1BQUQvczdQL2ZIeWtBQUQvd01EL2pJei91N3VDQUFDLzZmeVVBQUFCcDBsRVFWUjRuTzNheTFLRE1CaUEwVnFrRjYzVzkzOWFOeDA3MDBoTVFpQ3BuRzlMSWYrWkxESU0zZTBrU1pMeTI4K3I5ZmdKRVJMMkh5RmgveEUrbXpBYzhXVmUzWmtKQ1FrSkNRbTNKNnp1NmM1TVNFaElTRWk0WWVHYW5wVHFVd25YanBDUXNIMkVoSVR0SXlRa2JCOGg0WWFFSVd3ZlZHdldTQXRTQ1FrSkNRa0pDWC83U2hHNVZBWkxXVFN5QkNFaElTRWhJV0d4TUFKYlFWaTJSQjZWa0pDUWtKQ1FNRTBZL3JpNk1Hc0pRa0pDUWtKQ3dtSmhlSCtrTEdIV2t3a0pDUWtKQ1FuWEVmWWZJV0gvRVJMMkh5RmgrS0ExSXlRa0pGdytRa0xDUDU1NEdhdDBtVWxkVURnT1ZSb0pDUWtKQ1FrRFVOdnpNSVU2VDVpMFl5bkNRaGdoSVNFaElTRWhJU0VoSVNFaDRiTEM4NjJoRjJHRVdpUThmeHludWg1T0QzMnVBYXN1UEw1TzlmWitlT2hFU0VoSVNQaC9oRDlmdVdPeVcxOHB3bDdPdzV6TnZCLzBrOEM3TUxaMS9Rb2pNa0pDUWtMQ2JRbW4vK0EyVEwvNGhtL0FzWU4rUVdGSURlY0lMNVVWOFN3SUl5UWtKQ1FrSkN3MWw5WFlRMGhJU0VoSVNKaFdPR0pXcmNkUGlKQ3cvd2dKKzQvdytZV1NKRW1TSkVtU0pFbVNKS20wYjc4V3NjM3YzRjloQUFBQUFFbEZUa1N1UW1DQw==\u0022,\u0022pixelWidth\u0022:100,\u0022pixelHeight\u0022:100,\u0022contentType\u0022:\u0022image/png\u0022,\u0022bytesWritten\u0022:0},\u0022thumbnails\u0022:[{\u0022pixelWidth\u0022:400,\u0022pixelHeight\u0022:400,\u0022contentType\u0022:\u0022image/png\u0022,\u0022bytesWritten\u0022:20352}],\u0022uid\u0022:114230070024142849}]}","hdrTmpDriveAlias":"90f5e74a-b7f9-efda-0ac2-98373a32ad8c","hdrTmpDriveType":"90f5e74a-b7f9-efda-0ac2-98373a32ad8c","created":0,"modified":null}
                """;

            var driveMainRecord = OdinSystemSerializer.Deserialize<DriveMainIndexRecord>(driveMainRecordStr);

            var fm = ServerFileHeader.FromDriveMainIndexRecord(driveMainRecord);
            var sd = new StorageDrive("", "", new StorageDriveBase() { AllowAnonymousReads = true, Id = Guid.Parse("edee9397-a3d4-4981-b2cc-cdc685144b7b"), TargetDriveInfo = new TargetDrive() { Alias = new Core.GuidId(), Type = new Core.GuidId() } });
            var dr = fm.ToDriveMainIndexRecord(sd.TargetDriveInfo);

            var s2 = OdinSystemSerializer.Serialize<DriveMainIndexRecord>(dr);

            // Ok, I can't wrap my head around why in ServerFileHeader.FromDriveMainIndexRecord() we'd NOT copy over the localAppData field?

            ClassicAssert.IsTrue(dr.Equals(driveMainRecord));

            await Task.Delay(0);
        }
    }
}