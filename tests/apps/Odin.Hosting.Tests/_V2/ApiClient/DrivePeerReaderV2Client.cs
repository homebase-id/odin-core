using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class DrivePeerReaderV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<FileExistsOnPeerResponse>> GetFileExistsByUidAsync(OdinId peer, Guid driveId, Guid uid)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetFileExistsByUid(peer.DomainName, driveId, uid);
    }

    public async Task<ApiResponse<FileExistsOnPeerResponse>> GetFileExistsByGtidAsync(OdinId peer, Guid driveId, Guid gtid)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetFileExistsByGtid(peer.DomainName, driveId, gtid);
    }

    public async Task<ApiResponse<QueryBatchResponse>> QueryBatchAsync(OdinId peer, Guid driveId, QueryBatchRequest request,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.QueryBatch(peer.DomainName, driveId, request);
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsync(OdinId peer, Guid driveId, Guid fileId,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetFileHeader(peer.DomainName, driveId, fileId);
    }

    public async Task<ApiResponse<HttpContent>> GetPayloadAsync(OdinId peer, Guid driveId, Guid fileId, string payloadKey,
        FileChunk chunk = null, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        if (chunk == null)
        {
            return await svc.GetPayload(peer.DomainName, driveId, fileId, payloadKey);
        }

        return await svc.GetPayload(peer.DomainName, driveId, fileId, payloadKey, chunk.Start, chunk.Length);
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnailAsync(OdinId peer, Guid driveId, Guid fileId, string payloadKey,
        int width, int height, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetThumbnail(peer.DomainName, driveId, fileId, payloadKey, width, height);
    }

    // --- Read by GlobalTransitId ---

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByGtidAsync(OdinId peer, Guid driveId, Guid gtid,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetFileHeaderByGtid(peer.DomainName, driveId, gtid);
    }

    public async Task<ApiResponse<HttpContent>> GetPayloadByGtidAsync(OdinId peer, Guid driveId, Guid gtid, string payloadKey,
        FileChunk chunk = null, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        if (chunk == null)
        {
            return await svc.GetPayloadByGtid(peer.DomainName, driveId, gtid, payloadKey);
        }

        return await svc.GetPayloadByGtid(peer.DomainName, driveId, gtid, payloadKey, chunk.Start, chunk.Length);
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnailByGtidAsync(OdinId peer, Guid driveId, Guid gtid, string payloadKey,
        int width, int height, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetThumbnailByGtid(peer.DomainName, driveId, gtid, payloadKey, width, height);
    }

    // --- Temporal (time-boxed) read API ---

    public async Task<ApiResponse<TemporalAccessStatus>> VerifyTemporalAccessAsync(OdinId peer, Guid driveId,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.VerifyTemporalAccess(peer.DomainName, driveId);
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> TemporalGetFileHeaderAsync(OdinId peer, Guid driveId, Guid fileId,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fileSystemType);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.TemporalGetFileHeader(peer.DomainName, driveId, fileId);
    }
}
