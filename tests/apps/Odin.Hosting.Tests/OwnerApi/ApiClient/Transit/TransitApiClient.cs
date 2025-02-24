using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Core.Storage;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;


namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;

public class TransitApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public TransitApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task WaitForEmptyOutbox(TargetDrive drive, TimeSpan? maxWaitTime = null)
    {
        var maxWait = maxWaitTime ?? TimeSpan.FromSeconds(40);

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);

        var sw = Stopwatch.StartNew();
        while (true)
        {
            var response = await svc.GetDriveStatus(drive.Alias, drive.Type);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Error occured while retrieving outbox status");
            }

            var status = response.Content;
            if (status.Outbox.TotalItems == 0)
            {
                return;
            }

            if (sw.Elapsed > maxWait)
            {
                throw new TimeoutException($"timeout occured while waiting for outbox to complete processing");
            }

            await Task.Delay(100);
        }
    }

    public async Task ProcessInbox(TargetDrive drive)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await transitSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = drive });
            ClassicAssert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task AddReaction(TestIdentity recipient, GlobalTransitIdFileIdentifier file, string reactionContent)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<IRefitOwnerTransitReaction>(client, ownerSharedSecret);
            var resp = await transitSvc.AddReaction(new PeerAddReactionRequest()
            {
                OdinId = recipient.OdinId,
                Request = new AddRemoteReactionRequest()
                {
                    File = file,
                    Reaction = reactionContent
                }
            });

            ClassicAssert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task<GetReactionsPerimeterResponse> GetAllReactions(TestIdentity recipient, GetRemoteReactionsRequest request)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<IRefitOwnerTransitReaction>(client, ownerSharedSecret);
            var resp = await transitSvc.GetAllReactions(new PeerGetReactionsRequest()
            {
                OdinId = recipient.OdinId,
                Request = request
            });

            return resp.Content;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteReaction(TestIdentity recipient, string reaction, GlobalTransitIdFileIdentifier file)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var transitSvc = RefitCreator.RestServiceFor<IRefitOwnerTransitReaction>(client, ownerSharedSecret);
            var response = await transitSvc.DeleteReactionContent(new PeerDeleteReactionRequest()
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

    /// <summary>
    /// Directly sends the file to the recipients; does not store on any local drives.  (see DriveApiClient.TransferFile to store it on sender's drive)
    /// </summary>
    public async Task<TransitResult> TransferFileHeader(
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
            OverwriteGlobalTransitFileId = overwriteGlobalTransitFileId,
            RemoteTargetDrive = remoteTargetDrive,
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

            var transitService = RestService.For<IRefitOwnerTransitSender>(client);
            ApiResponse<TransitResult> response = await transitService.TransferStream(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var transitResult = response.Content;

            Assert.That(transitResult.RemoteGlobalTransitIdFileIdentifier, Is.Not.Null);
            Assert.That(transitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId, Is.Not.EqualTo(Guid.Empty));
            ClassicAssert.IsNotNull(transitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive);
            ClassicAssert.IsTrue(transitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive.IsValid());
            keyHeader.AesKey.Wipe();

            return transitResult;
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
            OverwriteGlobalTransitFileId = overwriteGlobalTransitFileId,
            RemoteTargetDrive = remoteTargetDrive,
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

            var transitSvc = RestService.For<IRefitOwnerTransitSender>(client);
            ApiResponse<TransitResult> response = await transitSvc.TransferStream(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var transitResult = response.Content;

            foreach (var recipient in recipients)
            {
                var status = transitResult.RecipientStatus[recipient];
                ClassicAssert.IsTrue(status == TransferStatus.Enqueued, $"failed to enqueue into outbox for {recipient}; status was {status}");
            }

            Assert.That(transitResult.RemoteGlobalTransitIdFileIdentifier, Is.Not.Null);
            Assert.That(transitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId, Is.Not.EqualTo(Guid.Empty));
            ClassicAssert.IsNotNull(transitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive);
            ClassicAssert.IsTrue(transitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive.IsValid());

            await this.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

            //Note: with the new outbox - there is no way to check the final status
            //of files sent using the peer direct send

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
            var transitSvc = RefitCreator.RestServiceFor<IRefitOwnerTransitSender>(client, sharedSecret);
            var response = await transitSvc.SendDeleteRequest(request);

            ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        }
    }


    //Query

    public async Task<ApiResponse<HttpContent>> GetPayloadOverTransit(OdinId remoteIdentity, ExternalFileIdentifier file, string key = WebScaffold.PAYLOAD_KEY,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerTransitQuery>(client, sharedSecret);

            var response = await svc.GetPayload(new TransitGetPayloadRequest()
            {
                OdinId = remoteIdentity,
                File = file,
                Key = key
            });

            return response;
        }
    }
}