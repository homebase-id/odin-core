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
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.OwnerApi.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Utils.Fluid;

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

    public async Task<UploadTestUtilsContext> Upload(TargetDrive targetDrive, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options = null)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);

        var instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new StorageOptions()
            {
                Drive = targetDrive,
                OverwriteFileId = null
            },
            TransitOptions = null
        };

        return await TransferFile(instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
    }

    public async Task<UploadTestUtilsContext> UploadFile(UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, string payloadData,
        bool encryptPayload = true, ImageDataContent thumbnail = null, KeyHeader keyHeader = null)
    {
        Assert.IsNull(instructionSet.TransitOptions?.Recipients, "This method will not send transfers; please ensure recipients are null");

        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret))
        {
            keyHeader = keyHeader ?? KeyHeader.NewRandom16();
            var instructionStream = new MemoryStream(DotYouSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            fileMetadata.PayloadIsEncrypted = encryptPayload;
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);
            var payloadCipherBytes = keyHeader.EncryptDataAes(payloadData.ToUtf8ByteArray());
            var payloadCipher = encryptPayload ? new MemoryStream(payloadCipherBytes) : new MemoryStream(payloadData.ToUtf8ByteArray());
            var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);

            ApiResponse<UploadResult> response;
            if (thumbnail == null)
            {
                response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));
            }
            else
            {
                var thumbnailCipherBytes = encryptPayload ? keyHeader.EncryptDataAesAsStream(thumbnail.Content) : new MemoryStream(thumbnail.Content);
                response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                    new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
            }

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var transferResult = response.Content;

            Assert.That(transferResult.File, Is.Not.Null);
            Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(transferResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

            //keyHeader.AesKey.Wipe();

            return new UploadTestUtilsContext()
            {
                InstructionSet = instructionSet,
                UploadFileMetadata = fileMetadata,
                PayloadData = payloadData,
                UploadedFile = transferResult.File,
                PayloadCipher = payloadCipherBytes
            };
        }
    }

    private async Task<UploadTestUtilsContext> TransferFile(UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options)
    {
        var recipients = instructionSet.TransitOptions?.Recipients ?? new List<string>();

        if (options.ProcessTransitBox & (recipients.Count == 0 || options.ProcessOutbox == false))
        {
            throw new Exception("Options not valid.  There must be at least one recipient and ProcessOutbox must be true when ProcessTransitBox is set to true");
        }

        var targetDrive = instructionSet.StorageOptions.Drive;

        //Feature added much later in schedule but it means we don't have to thread sleep in our unit tests
        if (options.ProcessOutbox && instructionSet.TransitOptions != null)
        {
            instructionSet.TransitOptions.Schedule = ScheduleOptions.SendNowAwaitResponse;
        }


        var payloadData = options?.PayloadData ?? "{payload:true, image:'b64 data'}";

        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret))
        {
            var keyHeader = KeyHeader.NewRandom16();
            var transferIv = instructionSet.TransferIv;

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            fileMetadata.PayloadIsEncrypted = options.EncryptPayload;
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);


            payloadData = options?.PayloadData ?? payloadData;
            Stream payloadCipher = options.EncryptPayload ? keyHeader.EncryptDataAesAsStream(payloadData) : new MemoryStream(payloadData.ToUtf8ByteArray());

            var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
            var response = await transitSvc.Upload(
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var transferResult = response.Content;

            Assert.That(transferResult.File, Is.Not.Null);
            Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(transferResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

            int outboxBatchSize = 1;
            if (instructionSet.TransitOptions?.Recipients?.Any() ?? false)
            {
                Assert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count, "expected recipient count does not match");

                foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                    Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated, $"transfer key not created for {recipient}");
                }

                outboxBatchSize = transferResult.RecipientStatus.Count;
            }

            // if (options is { ProcessOutbox: true })
            // {
            //     var resp = await transitSvc.ProcessOutbox(outboxBatchSize);
            //     Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
            // }

            if (options is { ProcessTransitBox: true })
            {
                //wait for process outbox to run
                // Task.Delay(2000).Wait();

                foreach (var recipient in recipients)
                {
                    //TODO: this should be a create app http client but it works because the path on ITransitTestAppHttpClient is /apps
                    using (var rClient = _ownerApi.CreateOwnerApiHttpClient((DotYouIdentity)recipient, out var _))
                    {
                        var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                        rClient.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());

                        var resp = await transitAppSvc.ProcessIncomingInstructions(new ProcessTransitInstructionRequest() { TargetDrive = targetDrive });
                        Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                    }
                }
            }

            keyHeader.AesKey.Wipe();

            return new UploadTestUtilsContext()
            {
                InstructionSet = instructionSet,
                UploadFileMetadata = fileMetadata,
                PayloadData = payloadData,
                UploadedFile = transferResult.File
            };
        }
    }
}