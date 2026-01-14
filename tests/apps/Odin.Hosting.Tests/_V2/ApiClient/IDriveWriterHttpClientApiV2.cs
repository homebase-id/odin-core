using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Drive;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveWriterHttpClientApiV2
{
    private const string FileIdEndpoint = UnifiedApiRouteConstants.ByFileId;

    [Patch(FileIdEndpoint + "/update-local-metadata-tags")]
    Task<ApiResponse<UpdateLocalMetadataResult>> UpdateLocalMetadataTags([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId, [Body] UpdateLocalMetadataTagsRequestV2 request);

    [Patch(FileIdEndpoint + "/update-local-metadata-content")]
    Task<ApiResponse<UpdateLocalMetadataResult>> UpdateLocalMetadataContent([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId, [Body] UpdateLocalMetadataContentRequestV2 request);

    [Post(FileIdEndpoint + "/delete")]
    Task<ApiResponse<DeleteFileResultV2>> SoftDeleteFile([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId, [Body] DeleteFileOptionsV2 options);

    [Post(FileIdEndpoint + "/delete-payload")]
    Task<ApiResponse<DeletePayloadResult>> DeletePayload([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId, [Body] DeletePayloadRequestV2 request);

    [Multipart]
    [Post(UnifiedApiRouteConstants.DrivesRoot + "/files")]
    Task<ApiResponse<CreateFileResult>> CreateNewFile([AliasAs("driveId:guid")] Guid driveId, StreamPart[] streamdata);

    [Multipart]
    [Patch(UnifiedApiRouteConstants.DrivesRoot + "/files")]
    Task<ApiResponse<UpdateFileResult>> UpdateFileByFileId([AliasAs("driveId:guid")] Guid driveId, [AliasAs("fileId:guid")] Guid fileId, StreamPart[] streamdata);

    [Post(UnifiedApiRouteConstants.FilesRoot + "/send-read-receipt-batch")]
    Task<ApiResponse<SendReadReceiptResultV2>> SendReadReceiptBatch([AliasAs("driveId:guid")] Guid driveId,
        [Body] SendReadReceiptRequestV2 request);

    [Post(UnifiedApiRouteConstants.FilesRoot + "/delete-batch/by-group-id")]
    Task<ApiResponse<DeleteFilesByGroupIdBatchResultV2>> DeleteFilesByGroupIdBatch([AliasAs("driveId:guid")] Guid driveId,
        [Body] DeleteFilesByGroupIdBatchRequestV2 request);

    [Post(UnifiedApiRouteConstants.FilesRoot + "/delete-batch/by-file-id")]
    Task<ApiResponse<DeleteFileIdBatchResultV2>> DeleteFileIdBatch([AliasAs("driveId:guid")] Guid driveId,
        [Body] DeleteFileIdBatchRequestV2 request);
}