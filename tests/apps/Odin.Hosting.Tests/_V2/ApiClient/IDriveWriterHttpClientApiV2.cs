using System;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IDriveWriterHttpClientApiV2
{
    private const string FilesRoot = UnifiedApiRouteConstants.FilesRoot;
    private const string FileIdEndpoint = UnifiedApiRouteConstants.ByFileId;

    [Patch(FileIdEndpoint + "/update-local-metadata-tags")]
    Task<ApiResponse<UpdateLocalMetadataResult>> UpdateLocalMetadataTags([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId, [Body] UpdateLocalMetadataTagsRequest request);

    [Patch(FileIdEndpoint + "/update-local-metadata-content")]
    Task<ApiResponse<UpdateLocalMetadataResult>> UpdateLocalMetadataContent([AliasAs("driveId:guid")] Guid driveId,
        [AliasAs("fileId:guid")] Guid fileId, [Body] UpdateLocalMetadataContentRequest request);

    [Multipart]
    [Post(FilesRoot + "/upload")]
    Task<ApiResponse<UploadResult>> UploadStream([AliasAs("driveId:guid")] Guid driveId, StreamPart[] streamdata);

    [Multipart]
    [Patch(FilesRoot + "/update")]
    Task<ApiResponse<FileUpdateResult>> UpdateFile([AliasAs("driveId:guid")] Guid driveId, StreamPart[] streamdata);
}