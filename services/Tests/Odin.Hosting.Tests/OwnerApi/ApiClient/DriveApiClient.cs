using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Drive.Management;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient;

public class DriveApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public DriveApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<OwnerClientDriveData> CreateDrive(TargetDrive targetDrive, string name, string metadata, bool allowAnonymousReads, bool ownerOnly = false,
        bool allowSubscriptions = false)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
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
                AllowSubscriptions = allowSubscriptions,
                OwnerOnly = ownerOnly
            });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });

            Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            Assert.NotNull(page);
            var theDrive = page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type);
            Assert.NotNull(theDrive);

            return theDrive;
        }
    }

    public async Task<SharedSecretEncryptedFileHeader> QueryByGlobalTransitFileId(FileSystemType fileSystemType, GlobalTransitIdFileIdentifier file)
    {
        var batch = await this.QueryBatch(fileSystemType, new FileQueryParams()
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

    public async Task<SharedSecretEncryptedFileHeader> QueryByUniqueId(FileSystemType fileSystemType, TargetDrive targetDrive, Guid uniqueId)
    {
        var batch = await this.QueryBatch(fileSystemType, new FileQueryParams()
        {
            TargetDrive = targetDrive,
            ClientUniqueIdAtLeastOne = new List<Guid>() { uniqueId }
        }, new QueryBatchResultOptionsRequest()
        {
            MaxRecords = 1,
            IncludeMetadataHeader = true
        });

        return batch.SearchResults.SingleOrDefault();
    }

    public async Task<QueryBatchResponse> QueryBatch(FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptionsRequest resultOptions = null)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
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

    public async Task<UploadResult> UploadFile(FileSystemType fileSystemType, TargetDrive targetDrive, UploadFileMetadata fileMetadata,
        string payloadData = "",
        Guid? overwriteFileId = null,
        ImageDataContent thumbnail = null)
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
                UseGlobalTransitId = true
            }
        };

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

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
                // if (!fileMetadata.AppData.AdditionalThumbnails.Any(t => t.PixelHeight == thumbnail.PixelHeight && t.PixelWidth == thumbnail.PixelWidth))
                // {
                //     throw new Exception("You sent a thumbnail but didnt specify it in your file data");
                // }

                parts.Add(new StreamPart(thumbnail.Content.ToMemoryStream(), thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
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

    public async Task<(UploadResult uploadResult, string encryptedJsonContent64, string encryptedPayloadContent64)> UploadEncryptedFile(FileSystemType fileSystemType, TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        string payloadData = "",
        ImageDataContent thumbnail = null,
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
                UseGlobalTransitId = true
            }
        };

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

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

            if (fileMetadata.AppData.ContentIsComplete && payloadData.Length > 0)
            {
                Assert.Inconclusive("ContentIsComplete is marked false but a payload was provided in the unit test");
            }

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

            return (uploadResult, encryptedJsonContent64, encryptedPayloadBytes.ToBase64());
        }
    }

    public async Task<SharedSecretEncryptedFileHeader> GetFileHeader(FileSystemType fileSystemType, ExternalFileIdentifier file)
    {
        return (await GetFileHeaderRaw(fileSystemType, file)).Content;
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderRaw(FileSystemType fileSystemType, ExternalFileIdentifier file)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
            var apiResponse = await svc.GetFileHeaderAsPost(file);
            return apiResponse;
        }
    }

    public async Task<HttpContent> GetPayload(FileSystemType fileSystemType, ExternalFileIdentifier file, FileChunk chunk = null)
    {
        return (await GetPayloadRaw(fileSystemType, file, chunk)).Content;
    }

    public async Task<ApiResponse<HttpContent>> GetPayloadRaw(FileSystemType fileSystemType, ExternalFileIdentifier file, FileChunk chunk = null)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            return await svc.GetPayloadPost(new GetPayloadRequest()
            {
                File = file,
                Chunk = chunk
            });
        }
    }


    public async Task DeleteFile(ExternalFileIdentifier file, List<string> recipients = null, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            var apiResponse = await svc.DeleteFile(new DeleteFileRequest()
            {
                File = file,
                Recipients = recipients
            });
        }
    }

    public async Task<DeletePayloadResult> DeletePayload(FileSystemType fileSystemType, ExternalFileIdentifier file)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
            var apiResponse = await svc.DeletePayload(new DeletePayloadRequest()
            {
                File = file
            });

            return apiResponse.Content;
        }
    }

    public async Task<DeleteThumbnailResult> DeleteThumbnail(FileSystemType fileSystemType, ExternalFileIdentifier file)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, sharedSecret);
            // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
            var apiResponse = await svc.DeleteThumbnail(new DeleteThumbnailRequest()
            {
                File = file
            });

            return apiResponse.Content;
        }
    }

    /// <summary>
    /// Uploads the file to the senders drive then sends it to the recipients
    /// </summary>
    public async Task<UploadResult> UploadAndTransferFile(FileSystemType fileSystemType,
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

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

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

    /// <summary>
    /// Uploads the file to the senders drive then sends it to the recipients
    /// </summary>
    public async Task<(UploadResult uploadResult, string encryptedJsonContent64)> UploadAndTransferEncryptedFile(
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

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fileSystemType);
        {
            var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

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

    public async Task<ApiResponse<PagedResult<OwnerClientDriveData>>> GetDrives(int pageNumber = 1, int pageSize = 100)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret);

        var driveSvc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, sharedSecret);
        return await driveSvc.GetDrives(new GetDrivesRequest() { PageNumber = pageNumber, PageSize = pageSize });
    }
}