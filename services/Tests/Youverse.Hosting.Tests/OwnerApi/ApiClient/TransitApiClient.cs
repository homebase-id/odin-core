using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.Drive;
using Youverse.Hosting.Tests.OwnerApi.Transit.Emoji;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.OwnerApi.ApiClient;

public class TransitApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public TransitApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task ProcessOutbox(int batchSize = 1)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
            var resp = await transitSvc.ProcessOutbox(batchSize);
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task ProcessIncomingInstructionSet(TargetDrive drive)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.ProcessIncomingInstructions(new ProcessTransitInstructionRequest() { TargetDrive = drive });
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task<UploadResult> TransferFile(FileSystemType fileSystemType,
        UploadFileMetadata fileMetadata,
        StorageOptions storageOptions,
        TransitOptions transitOptions,
        string payloadData = "",
        ImageDataContent thumbnail = null
    )
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = storageOptions,
            TransitOptions = transitOptions
        };

        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType))
        {
            var instructionStream = new MemoryStream(DotYouSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            // fileMetadata.AppData.JsonContent = keyHeader.EncryptDataAes(fileMetadata.AppData.JsonContent.ToUtf8ByteArray()).ToBase64();
            fileMetadata.PayloadIsEncrypted = false;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(new MemoryStream(payloadData.ToUtf8ByteArray()), "payload.encrypted", "application/x-binary",
                    Enum.GetName(MultipartUploadParts.Payload))
            };

            if (thumbnail != null)
            {
                var thumbnailCipherBytes = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                parts.Add(new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            }

            var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var uploadResult = response.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(uploadResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

            keyHeader.AesKey.Wipe();

            return uploadResult;
        }
    }

    public async Task<(UploadResult uploadResult, string encryptedJsonContent64)> TransferEncryptedFile(
        FileSystemType fileSystemType,
        UploadFileMetadata fileMetadata,
        StorageOptions storageOptions,
        TransitOptions transitOptions,
        string payloadData = "",
        ImageDataContent thumbnail = null)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = storageOptions,
            TransitOptions = transitOptions
        };

        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType))
        {
            var instructionStream = new MemoryStream(DotYouSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            var encryptedJsonContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.JsonContent.ToUtf8ByteArray()).ToBase64();
            fileMetadata.AppData.JsonContent = encryptedJsonContent64;
            fileMetadata.PayloadIsEncrypted = true;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            //expect a payload if the caller says there should be one
            byte[] encryptedPayloadBytes = Array.Empty<byte>();
            if (fileMetadata.AppData.ContentIsComplete == false)
            {
                encryptedPayloadBytes = keyHeader.EncryptDataAes(payloadData.ToUtf8ByteArray());
            }

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(new MemoryStream(encryptedPayloadBytes), "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload))
            };

            if (thumbnail != null)
            {
                var thumbnailCipherBytes = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                parts.Add(new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            }

            var driveSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            ApiResponse<UploadResult> response = await driveSvc.UploadStream(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var uploadResult = response.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(uploadResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

            keyHeader.AesKey.Wipe();

            return (uploadResult, encryptedJsonContent64);
        }
    }

    public async Task AddReaction(TestIdentity recipient, GlobalTransitIdFileIdentifier file, string reactionContent)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RefitCreator.RestServiceFor<ITransitEmojiHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.AddReaction(new TransitAddReactionRequest()
            {
                OdinId = recipient.OdinId,
                Request = new AddRemoteReactionRequest()
                {
                    File = file,
                    Reaction = reactionContent
                }
            });

            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task<GetReactionsResponse> GetAllReactions(TestIdentity recipient, GetRemoteReactionsRequest request)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RefitCreator.RestServiceFor<ITransitEmojiHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.GetAllReactions(new TransitGetReactionsRequest()
            {
                OdinId = recipient.OdinId,
                Request = request
            });

            return resp.Content;
        }
    }
}