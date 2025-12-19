using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Apps;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveReaderHttpClientApiV2
{
    private const string Endpoint = UnifiedApiRouteConstants.ByFileId;

    [Get(Endpoint + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId, FileSystemType fileSystemType);

    [Get(Endpoint + "/payload/{payloadKey}/{start:int}/{length:int}")]
    Task<ApiResponse<HttpContent>> GetPayload([AliasAs("driveId:guid")] Guid driveId, [AliasAs("fileId:guid")] Guid uid,
        [AliasAs("payloadKey")] string payloadKey,
        [AliasAs("start:int")] int start,
        [AliasAs("length:int")] int length,
        FileSystemType fileSystemType);


    [Get(Endpoint + "/payload/{payloadKey}")]
    Task<ApiResponse<HttpContent>> GetPayload([AliasAs("driveId:guid")] Guid driveId, [AliasAs("fileId:guid")] Guid uid,
        [AliasAs("payloadKey")] string payloadKey,
        FileSystemType fileSystemType);

    [Get(Endpoint + "/payload/{payloadKey}/thumb")]
    Task<ApiResponse<HttpContent>> GetThumbnail([AliasAs("driveId:guid")] Guid driveId, [AliasAs("fileId:guid")] Guid uid,
        [AliasAs("payloadKey")] string payloadKey,
        int? width,
        int? height,
        bool? directMatchOnly,
        FileSystemType fileSystemType);

    [Get(Endpoint + "/payload/{payloadKey}/thumb/{width}/{height}")]
    Task<ApiResponse<HttpContent>> GetThumbnail([AliasAs("driveId:guid")] Guid driveId, [AliasAs("fileId:guid")] Guid uid,
        [AliasAs("payloadKey")] string payloadKey,
        [AliasAs("width")] int width,
        [AliasAs("height")] int height,
        bool directMatchOnly,
        FileSystemType fileSystemType);

    [Get(Endpoint + "/transfer-history")]
    Task<ApiResponse<FileTransferHistoryResponse>> GetTransferHistory([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId, FileSystemType fileSystemType);
}