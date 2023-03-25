using System;
using System.Collections.Generic;
using System.IO;
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
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Controllers.OwnerToken.Transit;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.Transit.Emoji;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.OwnerApi.ApiClient.Transit;

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

    public async Task DeleteEmojiReaction(TestIdentity recipient, string reaction, GlobalTransitIdFileIdentifier file)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RefitCreator.RestServiceFor<ITransitEmojiHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.DeleteEmojiReaction(new TransitDeleteReactionRequest()
            {
                OdinId = recipient.OdinId,
                Request = new DeleteReactionRequestByGlobalTransitId()
                {
                    Reaction = reaction,
                    File = file
                }
            });
        }
    }

    public async Task DeleteAllReactionsOnFile(TestIdentity recipient, GlobalTransitIdFileIdentifier file)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RefitCreator.RestServiceFor<ITransitEmojiHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.DeleteAllReactionsOnFile(new TransitDeleteReactionRequest()
            {
                OdinId = recipient.OdinId,
                Request = new DeleteReactionRequestByGlobalTransitId()
                {
                    Reaction = "",
                    File = file
                }
            });
        }
    }

    public async Task<GetReactionCountsResponse> GetReactionCountsByFile(TestIdentity recipient, GetRemoteReactionsRequest request)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RefitCreator.RestServiceFor<ITransitEmojiHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.GetReactionCountsByFile(new TransitGetReactionsRequest()
            {
                OdinId = recipient.OdinId,
                Request = request
            });

            return resp.Content;
        }
    }

    public async Task<List<string>> GetReactionsByIdentity(TestIdentity recipient, OdinId identity, GlobalTransitIdFileIdentifier file)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var transitSvc = RefitCreator.RestServiceFor<ITransitEmojiHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.GetReactionsByIdentity(new TransitGetReactionsByIdentityRequest()
            {
                OdinId = recipient.OdinId,
                Identity = identity,
                File = file
            });

            return resp.Content;
        }
    }

    /// <summary>
    /// Directly sends the file to the recipients; does not store on any local drives.  (see DriveApiClient.TransferFile to store it on sender's drive)
    /// </summary>
    public async Task<TransitResult> TransferFile(
        FileSystemType fileSystemType,
        UploadFileMetadata fileMetadata,
        List<string> recipients,
        TargetDrive remoteTargetDrive,
        string payloadData = "",
        Guid? overwriteGlobalTransitFileId = null,
        ImageDataContent thumbnail = null
    )
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        TransitInstructionSet instructionSet = new TransitInstructionSet()
        {
            TransferIv = transferIv,
            GlobalTransitFileId = overwriteGlobalTransitFileId,
            RemoteTargetDrive = remoteTargetDrive,
            Schedule = ScheduleOptions.SendNowAwaitResponse,
            Recipients = recipients,
        };

        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType))
        {
            var instructionStream = new MemoryStream(DotYouSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

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

            var transitService = RestService.For<ITransitTestHttpClientForOwner>(client);
            ApiResponse<TransitResult> response = await transitService.TransferStream(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var transitResult = response.Content;

            Assert.That(transitResult.RemoteGlobalTransitIdFileIdentifier, Is.Not.Null);
            Assert.That(transitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId, Is.Not.EqualTo(Guid.Empty));
            Assert.IsNotNull(transitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive);
            Assert.IsTrue(transitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive.IsValid());
            keyHeader.AesKey.Wipe();

            return transitResult;
        }
    }

    /// <summary>
    /// Directly sends the file to the recipients; does not store on any local drives.  (see DriveApiClient.TransferEncryptedFile to store it on sender's drive)
    /// </summary>
    public async Task<(TransitResult transitResult, string encryptedJsonContent64)> TransferEncryptedFile(
        FileSystemType fileSystemType,
        UploadFileMetadata fileMetadata,
        List<string> recipients,
        TargetDrive remoteTargetDrive,
        string payloadData = "",
        Guid? overwriteGlobalTransitFileId = null,
        ImageDataContent thumbnail = null
    )
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();


        TransitInstructionSet instructionSet = new TransitInstructionSet()
        {
            TransferIv = transferIv,
            GlobalTransitFileId = overwriteGlobalTransitFileId,
            RemoteTargetDrive = remoteTargetDrive,
            Schedule = ScheduleOptions.SendNowAwaitResponse,
            Recipients = recipients,
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

            var transitSvc = RestService.For<ITransitTestHttpClientForOwner>(client);
            ApiResponse<TransitResult> response = await transitSvc.TransferStream(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var transitResult = response.Content;

            Assert.That(transitResult.RemoteGlobalTransitIdFileIdentifier, Is.Not.Null);
            Assert.That(transitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId, Is.Not.EqualTo(Guid.Empty));
            Assert.IsNotNull(transitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive);
            Assert.IsTrue(transitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive.IsValid());


            keyHeader.AesKey.Wipe();

            return (transitResult, encryptedJsonContent64);
        }
    }

    public async Task DeleteFile(FileSystemType fileSystemType, GlobalTransitIdFileIdentifier remoteGlobalTransitIdFileIdentifier,
        List<string> recipients)
    {
        var request = new DeleteFileByGlobalTransitIdRequest()
        {
            FileSystemType = fileSystemType,
            GlobalTransitIdFileIdentifier = remoteGlobalTransitIdFileIdentifier,
            Recipients = recipients,
        };

        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType))
        {
            var transitSvc = RefitCreator.RestServiceFor<ITransitTestHttpClientForOwner>(client, sharedSecret);
            var response = await transitSvc.SendDeleteRequest(request);

            Assert.IsTrue(response.IsSuccessStatusCode);
        }
    }
}