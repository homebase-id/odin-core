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
    }
}