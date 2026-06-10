using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests._Universal.DriveStorage;

/// <summary>
/// Shared scenario logic for inbox-promotion characterization tests.
/// The backend (disk vs S3) is decided entirely by the scaffold's configuration,
/// not by anything in this class.
/// </summary>
internal static class InboxPromotionScenario
{
    /// <summary>
    /// Runs an ENCRYPTED Frodo->Sam peer transfer of a file with one payload + one thumbnail,
    /// drains the outbox, asserts the file is NOT queryable before ProcessInbox (proves the inbox
    /// path was exercised rather than direct-write), processes the inbox, then asserts the
    /// promoted payload + thumbnail decrypt byte-equal to what was uploaded.
    /// </summary>
    public static async Task AssertEncryptedPeerTransferPromotesPayloadAndThumbnail(WebScaffold scaffold)
    {
        var senderOwnerClient = scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipientOwnerClient = scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        var targetDrive = TargetDrive.NewTargetDrive();

        // 1. Create the same target drive on both identities.
        var recipientDriveResponse = await recipientOwnerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on recipient",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

        var senderDriveResponse = await senderOwnerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on sender",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(senderDriveResponse.IsSuccessStatusCode);

        // 2. Recipient creates a circle granting Write on the target drive.
        var circleId = Guid.NewGuid();
        var createCircleResponse = await recipientOwnerClient.Network.CreateCircle(
            circleId,
            "Circle with drive write access",
            new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest>
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive
                        {
                            Drive = targetDrive,
                            Permission = DrivePermission.Write
                        }
                    }
                }
            });
        ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode);

        // 3. Connect: sender sends request, recipient accepts into the circle.
        await senderOwnerClient.Connections.SendConnectionRequest(
            recipientOwnerClient.Identity.OdinId, new List<GuidId>());
        await recipientOwnerClient.Connections.AcceptConnectionRequest(
            senderOwnerClient.Identity.OdinId, new List<GuidId> { circleId });

        // 4. Frodo uploads an ENCRYPTED file with one payload + one thumbnail,
        //    distributed to Sam. Encryption forces the transfer through the inbox
        //    (CanDirectWriteFile returns false for encrypted files when the caller
        //    does not have the recipient's drive storage key inline).
        //
        //    We build the encrypted upload inline, mirroring the pattern in
        //    UniversalDriveApiClient.UploadNewEncryptedFile but passing TransitOptions
        //    with the recipient, since no existing helper combines both.
        var callerContext = new OwnerClientContext(targetDrive);
        await callerContext.Initialize(senderOwnerClient);
        var factory = callerContext.GetFactory();

        var payloadDef = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        // Per-payload IV is required for encrypted uploads.
        payloadDef.Iv = ByteArrayUtil.GetRndByteArray(16);
        var testPayloads = new List<TestPayloadDefinition> { payloadDef };

        var uploadManifest = new UploadManifest
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new UploadAppFileMetaData
            {
                Content = "characterization-test-content",
                FileType = 4242
            },
            AccessControlList = AccessControlList.Connected
        };

        var transitOptions = new TransitOptions
        {
            Recipients = new List<string> { recipientOwnerClient.Identity.OdinId }
        };

        var keyHeader = KeyHeader.NewRandom16();
        var transferIv = ByteArrayUtil.GetRndByteArray(16);

        var instructionSet = new UploadInstructionSet
        {
            TransferIv = transferIv,
            StorageOptions = new StorageOptions { Drive = targetDrive },
            TransitOptions = transitOptions,
            Manifest = uploadManifest
        };

        ApiResponse<UploadResult> uploadResponse;
        using (var httpClient = factory.CreateHttpClient(senderOwnerClient.Identity.OdinId, out var sharedSecret))
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            // Encrypt the app-data content and mark the file as encrypted.
            fileMetadata.AppData.Content =
                keyHeader.EncryptDataAes(fileMetadata.AppData.Content.ToUtf8ByteArray()).ToBase64();

            var descriptor = new UploadFileDescriptor
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

            var parts = new List<StreamPart>
            {
                new(instructionStream, "instructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Instructions)),
                new(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json",
                    Enum.GetName(MultipartUploadParts.Metadata))
            };

            // Encrypt each payload and its thumbnails using the per-payload IV.
            foreach (var pd in testPayloads)
            {
                var payloadKeyHeader = new KeyHeader
                {
                    Iv = pd.Iv,
                    AesKey = new SensitiveByteArray(keyHeader.AesKey.GetKey())
                };

                var payloadCipher = payloadKeyHeader.EncryptDataAesAsStream(pd.Content);
                parts.Add(new StreamPart(payloadCipher, pd.Key, pd.ContentType,
                    Enum.GetName(MultipartUploadParts.Payload)));

                foreach (var thumb in pd.Thumbnails ?? new List<ThumbnailContent>())
                {
                    var thumbCipher = payloadKeyHeader.EncryptDataAesAsStream(thumb.Content);
                    var thumbKey = $"{pd.Key}{thumb.PixelWidth}{thumb.PixelHeight}";
                    parts.Add(new StreamPart(thumbCipher, thumbKey, thumb.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));
                }
            }

            var driveSvc = RestService.For<IUniversalDriveHttpClientApi>(httpClient);
            uploadResponse = await driveSvc.UploadStream(parts.ToArray());
        }

        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode,
            $"Upload failed: {uploadResponse.StatusCode}");
        var uploadResult = uploadResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);
        ClassicAssert.IsTrue(
            uploadResult.RecipientStatus.ContainsKey(recipientOwnerClient.Identity.OdinId),
            "RecipientStatus should contain Sam");
        ClassicAssert.IsTrue(
            uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId] == TransferStatus.Enqueued,
            $"Expected Enqueued, got {uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId]}");

        // 5. Drain sender outbox.
        await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

        // 5a. INBOX-PATH GATE: before ProcessInbox the file must NOT be queryable on
        //     the recipient. If it already appears here the transfer went through
        //     direct-write (bypassing inbox staging) and this test is NOT characterizing
        //     the inbox-promote path.
        var preInboxResponse = await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(
            uploadResult.GlobalTransitIdFileIdentifier);
        ClassicAssert.IsTrue(preInboxResponse.IsSuccessStatusCode);
        ClassicAssert.IsEmpty(
            preInboxResponse.Content.SearchResults,
            "File must NOT be queryable before ProcessInbox: transfer went through direct-write instead of inbox");

        // 6. Process recipient inbox, then locate the promoted file.
        await recipientOwnerClient.DriveRedux.ProcessInbox(targetDrive);

        var recipientFileResponse = await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(
            uploadResult.GlobalTransitIdFileIdentifier);

        ClassicAssert.IsTrue(recipientFileResponse.IsSuccessStatusCode);
        var recipientHeader = recipientFileResponse.Content.SearchResults.SingleOrDefault();
        ClassicAssert.IsNotNull(recipientHeader,
            "Recipient should have received the file after inbox processing");

        // Build the recipient's ExternalFileIdentifier (different FileId from sender's).
        var recipientFile = new ExternalFileIdentifier
        {
            FileId = recipientHeader.FileId,
            TargetDrive = recipientHeader.TargetDrive
        };

        // 7. Assert the payload is present and byte-equal after decryption.
        //    The server stores encrypted bytes; we decrypt with the original keyHeader
        //    and the per-payload IV recorded in the file header.
        ClassicAssert.IsTrue(recipientHeader.FileMetadata.Payloads.Any(),
            "Recipient file header should list at least one payload");

        var payloadDescriptor = recipientHeader.FileMetadata.Payloads
            .Single(p => p.Key == payloadDef.Key);
        var payloadKeyHeaderForDecrypt = new KeyHeader
        {
            Iv = payloadDescriptor.Iv,
            AesKey = new SensitiveByteArray(keyHeader.AesKey.GetKey())
        };

        var payloadResponse = await recipientOwnerClient.DriveRedux.GetPayload(
            recipientFile, payloadDef.Key);
        ClassicAssert.IsTrue(payloadResponse.IsSuccessStatusCode,
            $"GetPayload on recipient returned {payloadResponse.StatusCode}");

        var encryptedPayloadBytes = (await payloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
        var decryptedPayloadBytes = payloadKeyHeaderForDecrypt.Decrypt(encryptedPayloadBytes);
        CollectionAssert.AreEqual(payloadDef.Content, decryptedPayloadBytes,
            "Decrypted payload bytes on recipient must be byte-equal to what was uploaded");

        // 8. Assert the thumbnail is present and byte-equal after decryption.
        ClassicAssert.IsTrue(
            recipientHeader.FileMetadata.Payloads.Any(p => p.Thumbnails.Any()),
            "Recipient file header should list at least one thumbnail under the payload");

        var thumbnail = payloadDef.Thumbnails.First();
        var thumbnailResponse = await recipientOwnerClient.DriveRedux.GetThumbnail(
            recipientFile, thumbnail.PixelWidth, thumbnail.PixelHeight, payloadDef.Key);
        ClassicAssert.IsTrue(thumbnailResponse.IsSuccessStatusCode,
            $"GetThumbnail on recipient returned {thumbnailResponse.StatusCode}");

        var encryptedThumbnailBytes = (await thumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
        var decryptedThumbnailBytes = payloadKeyHeaderForDecrypt.Decrypt(encryptedThumbnailBytes);
        CollectionAssert.AreEqual(thumbnail.Content, decryptedThumbnailBytes,
            "Decrypted thumbnail bytes on recipient must be byte-equal to what was uploaded");

        // Cleanup
        await scaffold.OldOwnerApi.DisconnectIdentities(
            senderOwnerClient.Identity.OdinId,
            recipientOwnerClient.Identity.OdinId);
    }
}
