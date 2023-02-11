using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.OwnerToken.Drive;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.OwnerApi.Drive;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.OwnerApi.ApiClient;

public class DriveApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public DriveApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<OwnerClientDriveData> CreateDrive(TargetDrive targetDrive, string name, string metadata, bool allowAnonymousReads, bool ownerOnly = false)
    {
        using (var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

            if (ownerOnly && allowAnonymousReads)
            {
                throw new Exception("cannot have an owner only drive that allows anonymous reads");
            }

            var response = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = name,
                Metadata = metadata,
                AllowAnonymousReads = allowAnonymousReads,
                OwnerOnly = ownerOnly
            });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });

            Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            Assert.NotNull(page);
            var theDrive = page.Results.SingleOrDefault(drive => drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type);
            Assert.NotNull(theDrive);

            return theDrive;
        }
    }
    
	public async Task<UploadResult> UploadReactionFile(TargetDrive targetDrive, UploadFileMetadata fileMetadata)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret))
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

            var keyHeader = KeyHeader.NewRandom16();

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

            var uploadService = RestService.For<IDriveReactionHttpTestClientForOwner>(client);
            var response = await uploadService.UploadTextReaction(
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(new MemoryStream(), "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);

            return response.Content;
        }
    }

    public async Task<QueryBatchResponse> QueryBatch(FileQueryParams qp, QueryBatchResultOptionsRequest resultOptions = null)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);

            var ro = resultOptions ?? new QueryBatchResultOptionsRequest()
            {
                CursorState = "",
                MaxRecords = 10,
                IncludeMetadataHeader = true
            };

            var request = new QueryBatchRequest()
            {
                QueryParams = qp,
                ResultOptionsRequest = ro
            };

            var response = await svc.GetBatch(request);
            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            var batch = response.Content;
            Assert.IsNotNull(batch);

            return batch;
        }
    }

    public async Task<UploadResult> UploadMetadataFile(TargetDrive targetDrive, UploadFileMetadata fileMetadata)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret))
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

            var keyHeader = KeyHeader.NewRandom16();

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

            var uploadService = RestService.For<IDriveTestHttpClientForOwner>(client);
            var response = await uploadService.Upload(
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(new MemoryStream(), "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);

            return response.Content;
        }
    }

    public async Task<UploadTestUtilsContext> UploadMetadataFile(UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, string payloadData,
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

    public async Task<ClientFileHeader> GetFileHeader(ExternalFileIdentifier file)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            var apiResponse = await svc.GetFileHeader(file);

            Assert.IsTrue(apiResponse.IsSuccessStatusCode, "Server failure when getting file header");
            return apiResponse.Content;
        }
    }

    public async Task<ClientFileHeader> GetTextReactionHeader(ExternalFileIdentifier reactionFile)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<IDriveReactionHttpTestClientForOwner>(client, sharedSecret);
            var apiResponse = await svc.GetTextReactionHeader(reactionFile.FileId, reactionFile.TargetDrive.Alias, reactionFile.TargetDrive.Type);

            Assert.IsTrue(apiResponse.IsSuccessStatusCode, "Server failure when getting file header");
            return apiResponse.Content;
        }
    }
}