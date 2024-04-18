using System.Threading.Tasks;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive
{
    /// <summary>
    /// Base class for querying a drive's search index
    /// </summary>
    public abstract class DriveQueryControllerBase : OdinControllerBase
    {
        protected async Task<QueryModifiedResult> QueryModified(QueryModifiedRequest request)
        {
            var driveId = TheOdinContext.PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetModified(driveId, request.QueryParams, request.ResultOptions, TheOdinContext);
            return batch;
        }

        protected async Task<QueryBatchResponse> QueryBatch(QueryBatchRequest request)
        {
            var driveId = TheOdinContext.PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions(), TheOdinContext);
            return QueryBatchResponse.FromResult(batch);
        }

        protected async Task<QueryBatchCollectionResponse> QueryBatchCollection(QueryBatchCollectionRequest request)
        {
            var collection = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetBatchCollection(request, TheOdinContext);
            return collection;
        }
    }
}