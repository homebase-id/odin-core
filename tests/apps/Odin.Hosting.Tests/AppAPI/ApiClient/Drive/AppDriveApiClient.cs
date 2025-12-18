using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.Apps;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Drive;

public class AppDriveApiClient : AppApiClientBase
{
    private readonly AppClientToken _token;

    public AppDriveApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }

    public async Task<SharedSecretEncryptedFileHeader> QueryByGlobalTransitFileId(FileSystemType fileSystemType, GlobalTransitIdFileIdentifier file)
    {
        var batch = await this.QueryBatch(fileSystemType, new FileQueryParamsV1()
        {
            TargetDrive = file.TargetDrive,
            GlobalTransitId = new List<Guid>() { file.GlobalTransitId }
        }, new QueryBatchResultOptionsRequest()
        {
            MaxRecords = 10,
            IncludeMetadataHeader = true
        });

        return batch.SearchResults.SingleOrDefault();
    }

    public async Task<QueryBatchResponse> QueryBatch(FileSystemType fileSystemType, FileQueryParamsV1 qp, QueryBatchResultOptionsRequest resultOptions = null)
    {
        var client = CreateAppApiHttpClient(_token, fileSystemType);
        {
            var svc = CreateDriveService(client);

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
            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            var batch = response.Content;
            ClassicAssert.IsNotNull(batch);

            return batch;
        }
    }

    public async Task<ApiResponse<QueryBatchCollectionResponse>> QueryBatchCollection(FileSystemType fileSystemType,
        List<CollectionQueryParamSection> querySections)
    {
        var client = CreateAppApiHttpClient(_token, fileSystemType);
        {
            var svc = CreateDriveService(client);

            var response = await svc.QueryBatchCollection(new QueryBatchCollectionRequest()
            {
                Queries = querySections
            });

            return response;
        }
    }


    public async Task<ApiResponse<UploadResult>> UpdateMetadataRaw(TargetDrive targetDrive, UploadFileMetadata fileMetadata,
        Guid? overwriteFileId = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var (_, response) = await this.UploadUnEncryptedMetadataInternal(fileSystemType, targetDrive, fileMetadata, overwriteFileId: overwriteFileId);
        return response;
    }

    public async Task<UploadResult> UpdateMetadata(TargetDrive targetDrive, UploadFileMetadata fileMetadata,
        Guid? overwriteFileId = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var (_, response) = await this.UploadUnEncryptedMetadataInternal(fileSystemType, targetDrive, fileMetadata, overwriteFileId: overwriteFileId);

        var uploadResult = response.Content;
        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(uploadResult, Is.Not.Null);
        Assert.That(uploadResult.File, Is.Not.Null);
        Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(uploadResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

        return uploadResult;
    }

    public async Task<UploadResult> UploadFile(TargetDrive targetDrive, UploadFileMetadata fileMetadata,
        string payloadData = "",
        List<ThumbnailContent> thumbnails = null,
        Guid? overwriteFileId = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var (_, response) = await this.UploadUnEncryptedFileInternal(fileSystemType, targetDrive, fileMetadata, payloadData, thumbnails, overwriteFileId);

        var uploadResult = response.Content;
        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(uploadResult, Is.Not.Null);
        Assert.That(uploadResult.File, Is.Not.Null);
        Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(uploadResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

        return uploadResult;
    }

    public async Task<(UploadInstructionSet uploadedInstructionSet, ApiResponse<UploadResult>)> UploadRaw(FileSystemType fileSystemType,
        TargetDrive targetDrive, UploadFileMetadata fileMetadata,
        string payloadData = "",
        List<ThumbnailContent> thumbnails = null,
        Guid? overwriteFileId = null)
    {
        var (uploadedInstructionSet, response) =
            await this.UploadUnEncryptedFileInternal(fileSystemType, targetDrive, fileMetadata, payloadData, thumbnails, overwriteFileId);
        return (uploadedInstructionSet, response);
    }


    public async Task<(UploadResult uploadResult, KeyHeader keyHeader, string encryptedJsonContent64)> UploadEncryptedFile(FileSystemType fileSystemType,
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        string payloadData = "",
        List<ThumbnailContent> thumbnails = null,
        Guid? overwriteFileId = null)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
                OverwriteFileId = overwriteFileId
            },
            TransitOptions = new TransitOptions()
            {
            }
        };

        var client = CreateAppApiHttpClient(_token, fileSystemType);
        {
            var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();

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

            //expect a payload if the caller says there should be one
            byte[] encryptedPayloadBytes = Array.Empty<byte>();
            if (!string.IsNullOrEmpty(payloadData))
            {
                encryptedPayloadBytes = keyHeader.EncryptDataAes(payloadData.ToUtf8ByteArray());
            }

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                new StreamPart(new MemoryStream(encryptedPayloadBytes), WebScaffold.PAYLOAD_KEY, "application/x-binary",
                    Enum.GetName(MultipartUploadParts.Payload))
            };

            if (thumbnails?.Any() ?? false)
            {
                foreach (var thumbnail in thumbnails)
                {
                    var thumbnailCipherBytes = keyHeader.EncryptDataAesAsStream(thumbnail.Content);
                    parts.Add(
                        new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
                }
            }

            var driveSvc = CreateDriveService(client);
            ApiResponse<UploadResult> response = await driveSvc.Upload(parts.ToArray());

            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content, Is.Not.Null);
            var uploadResult = response.Content;

            Assert.That(uploadResult.File, Is.Not.Null);
            Assert.That(uploadResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(uploadResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

            return (uploadResult, keyHeader, encryptedJsonContent64);
        }
    }

    public Task<ApiResponse<UploadPayloadResult>> UploadAttachments(ExternalFileIdentifier targetFile,
        List<ThumbnailContent> thumbnails,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        //
        // var client = CreateAppApiHttpClient(_token, fileSystemType);
        // {
        //     var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();
        //     var driveSvc = RestService.For<IDriveTestHttpClientForApps>(client);
        //
        //     var response = await driveSvc.UploadPayloads(parts.ToArray());
        //
        //     return (instructionSet, response);
        // }
        return Task.FromResult<ApiResponse<UploadPayloadResult>>(null);
    }

    public async Task<SharedSecretEncryptedFileHeader> GetFileHeader(ExternalFileIdentifier file, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = CreateDriveService(client);
            // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
            var apiResponse = await svc.GetFileHeaderAsPost(file);
            return apiResponse.Content;
        }
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnail(ExternalFileIdentifier file, int width, int height, string payloadKey,
        bool directMatchOnly = false,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fileSystemType);
        {
            var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();
            var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, sharedSecret);

            var thumbnailResponse = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
            {
                File = file,
                Height = height,
                Width = width,
                PayloadKey = payloadKey,
                DirectMatchOnly = directMatchOnly
            });

            return thumbnailResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> GetPayload(ExternalFileIdentifier file, FileChunk chunk = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fileSystemType);
        {
            var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();
            var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, sharedSecret);

            var thumbnailResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest()
            {
                File = file,
                Chunk = chunk,
                Key = WebScaffold.PAYLOAD_KEY
            });

            return thumbnailResponse;
        }
    }

    public async Task DeleteFile(FileSystemType fileSystemType, ExternalFileIdentifier file, List<string> recipients = null)
    {
        var client = CreateAppApiHttpClient(_token, fileSystemType);
        {
            var svc = CreateDriveService(client);
            // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
            var apiResponse = await svc.DeleteFile(new DeleteFileRequest()
            {
                File = file,
                Recipients = recipients
            });
        }
    }

    //
    private async Task<(UploadInstructionSet uploadedInstructionSet, ApiResponse<UploadResult> response)> UploadUnEncryptedMetadataInternal(
        FileSystemType fileSystemType,
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        Guid? overwriteFileId = null)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
                OverwriteFileId = overwriteFileId,
                StorageIntent = StorageIntent.MetadataOnly
            },
            TransitOptions = new TransitOptions()
            {
            }
        };

        var client = CreateAppApiHttpClient(_token, fileSystemType);
        {
            var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();

            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

            fileMetadata.IsEncrypted = false;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = null,
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata))
            };

            var driveSvc = RestService.For<IDriveTestHttpClientForApps>(client);
            ApiResponse<UploadResult> response = await driveSvc.Upload(parts.ToArray());
            keyHeader.AesKey.Wipe();
            return (instructionSet, response);
        }
    }

    private async Task<(UploadInstructionSet uploadedInstructionSet, ApiResponse<UploadResult> response)> UploadUnEncryptedFileInternal(
        FileSystemType fileSystemType,
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        string payloadData = "",
        List<ThumbnailContent> thumbnails = null,
        Guid? overwriteFileId = null)
    {
        var transferIv = ByteArrayUtil.GetRndByteArray(16);
        var keyHeader = KeyHeader.NewRandom16();

        bool hasPayload = !string.IsNullOrEmpty(payloadData);
        string payloadKey = WebScaffold.PAYLOAD_KEY;

        UploadInstructionSet instructionSet = new UploadInstructionSet()
        {
            TransferIv = transferIv,
            StorageOptions = new()
            {
                Drive = targetDrive,
                OverwriteFileId = overwriteFileId
            },
            TransitOptions = new TransitOptions()
            {
            },
            Manifest = new UploadManifest()
        };

        var client = CreateAppApiHttpClient(_token, fileSystemType);
        {
            var sharedSecret = _token.SharedSecret.ToSensitiveByteArray();

            if(hasPayload)
            {
                instructionSet.Manifest.PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
                {
                    new()
                    {
                        PayloadKey = payloadKey,
                        DescriptorContent = "",
                        Thumbnails = new List<UploadedManifestThumbnailDescriptor>()
                    }
                };
            }
            
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());
            fileMetadata.IsEncrypted = false;

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);
            
            // var bytesUploaded = instructionStream.Length + fileDescriptorCipher.Length + payloadData.Length;
            List<StreamPart> parts = new()
            {
                new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
            };
            
            var payloadStream = new MemoryStream(payloadData.ToUtf8ByteArray());
            if(hasPayload)
            {
                parts.Add(new StreamPart(payloadStream, payloadKey, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));
            }


            if (thumbnails?.Any() ?? false)
            {
                foreach (var thumbnail in thumbnails)
                {
                    parts.Add(new StreamPart(new MemoryStream(thumbnail.Content), thumbnail.GetFilename(), thumbnail.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));
                }
            }

            var driveSvc = RestService.For<IDriveTestHttpClientForApps>(client);
            ApiResponse<UploadResult> response = await driveSvc.Upload(parts.ToArray());
            keyHeader.AesKey.Wipe();
            return (instructionSet, response);
        }
    }

    private IDriveTestHttpClientForApps CreateDriveService(HttpClient client)
    {
        return RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, _token.SharedSecret);
    }
}