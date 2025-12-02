using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class DriveV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsync(ExternalFileIdentifier file,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDriveHttpClientApiV2>(client, sharedSecret);
        var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetPayloadAsync(ExternalFileIdentifier file, string key, FileChunk chunk = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDriveHttpClientApiV2>(client, sharedSecret);
        return await svc.GetPayload(file.FileId, file.TargetDrive.Alias, key, chunk?.Start ?? 0, chunk?.Length ?? 0);
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnailAsync(ExternalFileIdentifier file, int width, int height, string payloadKey,
        bool directMatchOnly = false, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDriveHttpClientApiV2>(client, sharedSecret);

        var thumbnailResponse = await svc.GetThumbnail(file.FileId, file.TargetDrive.Alias, payloadKey, width, height);

        return thumbnailResponse;
    }
}