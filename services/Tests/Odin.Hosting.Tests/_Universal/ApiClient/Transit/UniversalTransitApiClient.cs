using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.Incoming;
using Odin.Core.Services.Peer.Incoming.Drive;
using Odin.Core.Services.Peer.Incoming.Reactions;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Reactions;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Transit;

public class UniversalTransitApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public UniversalTransitApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task ProcessOutbox(int batchSize = 1)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            client.DefaultRequestHeaders.Add(SystemAuthConstants.Header, _ownerApi.SystemProcessApiKey.ToString());
            var resp = await transitSvc.ProcessOutbox(batchSize);
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task ProcessInbox(TargetDrive drive)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = drive });
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task<ApiResponse<HttpContent>> AddReaction(TestIdentity recipient, GlobalTransitIdFileIdentifier file, string reactionContent)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<OwnerApi.ApiClient.Transit.IRefitOwnerTransitReaction>(client, ownerSharedSecret);
            var response = await transitSvc.AddReaction(new TransitAddReactionRequest()
            {
                OdinId = recipient.OdinId,
                Request = new AddRemoteReactionRequest()
                {
                    File = file,
                    Reaction = reactionContent
                }
            });

            return response;
        }
    }

    public async Task<ApiResponse<GetReactionsPerimeterResponse>> GetAllReactions(TestIdentity recipient, GetRemoteReactionsRequest request)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<OwnerApi.ApiClient.Transit.IRefitOwnerTransitReaction>(client, ownerSharedSecret);
            var resp = await transitSvc.GetAllReactions(new TransitGetReactionsRequest()
            {
                OdinId = recipient.OdinId,
                Request = request
            });

            return resp;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteReaction(TestIdentity recipient, string reaction, GlobalTransitIdFileIdentifier file)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<OwnerApi.ApiClient.Transit.IRefitOwnerTransitReaction>(client, ownerSharedSecret);
            var response = await transitSvc.DeleteReactionContent(new TransitDeleteReactionRequest()
            {
                OdinId = recipient.OdinId,
                Request = new DeleteReactionRequestByGlobalTransitId()
                {
                    Reaction = reaction,
                    File = file
                }
            });

            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteAllReactionsOnFile(TestIdentity recipient, GlobalTransitIdFileIdentifier file)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<OwnerApi.ApiClient.Transit.IRefitOwnerTransitReaction>(client, ownerSharedSecret);
            var resp = await transitSvc.DeleteAllReactionsOnFile(new TransitDeleteReactionRequest()
            {
                OdinId = recipient.OdinId,
                Request = new DeleteReactionRequestByGlobalTransitId()
                {
                    Reaction = "",
                    File = file
                }
            });

            return resp;
        }
    }

    public async Task<ApiResponse<GetReactionCountsResponse>> GetReactionCountsByFile(TestIdentity recipient, GetRemoteReactionsRequest request)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<OwnerApi.ApiClient.Transit.IRefitOwnerTransitReaction>(client, ownerSharedSecret);
            var resp = await transitSvc.GetReactionCountsByFile(new TransitGetReactionsRequest()
            {
                OdinId = recipient.OdinId,
                Request = request
            });

            return resp;
        }
    }

    public async Task<List<string>> GetReactionsByIdentity(TestIdentity recipient, OdinId identity, GlobalTransitIdFileIdentifier file)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<OwnerApi.ApiClient.Transit.IRefitOwnerTransitReaction>(client, ownerSharedSecret);
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
    public async Task<ApiResponse<TransitResult>> TransferFileHeader(
        UploadFileMetadata fileMetadata,
        List<string> recipients,
        TargetDrive remoteTargetDrive,
        Guid? overwriteGlobalTransitFileId = null,
        ThumbnailContent thumbnail = null,
        FileSystemType fileSystemType = FileSystemType.Standard
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

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            fileMetadata.IsEncrypted = false;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            };

            if (thumbnail != null)
            {
                var thumbnailCipherBytes = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                parts.Add(new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            }

            var transitService = RestService.For<IUniversalRefitOwnerTransitSender>(client);
            ApiResponse<TransitResult> response = await transitService.TransferStream(parts.ToArray());

            return response;
        }
    }

    /// <summary>
    /// Directly sends the file to the recipients; does not store on any local drives.  (see DriveApiClient.TransferEncryptedFile to store it on sender's drive)
    /// </summary>
    public async Task<(TransitResult transitResult, string encryptedJsonContent64)> TransferEncryptedFileHeader(
        FileSystemType fileSystemType,
        UploadFileMetadata fileMetadata,
        List<string> recipients,
        TargetDrive remoteTargetDrive,
        Guid? overwriteGlobalTransitFileId = null,
        ThumbnailContent thumbnail = null
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

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            var encryptedJsonContent64 = keyHeader.EncryptDataAes(fileMetadata.AppData.Content.ToUtf8ByteArray()).ToBase64();
            fileMetadata.AppData.Content = encryptedJsonContent64;
            fileMetadata.IsEncrypted = true;

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
            };

            if (thumbnail != null)
            {
                var thumbnailCipherBytes = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                parts.Add(new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            }

            var transitSvc = RestService.For<OwnerApi.ApiClient.Transit.IRefitOwnerTransitSender>(client);
            ApiResponse<TransitResult> response = await transitSvc.TransferStream(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var transitResult = response.Content;

            foreach (var recipient in recipients)
            {
                var status = transitResult.RecipientStatus[recipient];
                bool wasDelivered = status == TransferStatus.DeliveredToInbox || status == TransferStatus.DeliveredToTargetDrive;
                Assert.IsTrue(wasDelivered, $"failed to deliver to {recipient}; status was {status}");
            }

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

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var transitSvc = RefitCreator.RestServiceFor<OwnerApi.ApiClient.Transit.IRefitOwnerTransitSender>(client, sharedSecret);
            var response = await transitSvc.SendDeleteRequest(request);

            Assert.IsTrue(response.IsSuccessStatusCode);
        }
    }
}