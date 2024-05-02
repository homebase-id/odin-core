using System.Threading.Tasks;
using Odin.Core.Storage.SQLite;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive
{
    /// <summary>
    /// Base class for querying a drive's search index
    /// </summary>
    public abstract class DriveQueryControllerBase : OdinControllerBase
    {
        protected async Task<QueryModifiedResult> QueryModified(QueryModifiedRequest request, DatabaseConnection cn)
        {
            var driveId = WebOdinContext.PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetModified(driveId, request.QueryParams, request.ResultOptions, WebOdinContext, cn);
            return batch;
        }

        protected async Task<QueryBatchResponse> QueryBatch(QueryBatchRequest request, DatabaseConnection cn)
        {
            var driveId = WebOdinContext.PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions(), WebOdinContext, cn);
            return QueryBatchResponse.FromResult(batch);
        }

        protected async Task<QueryBatchCollectionResponse> QueryBatchCollection(QueryBatchCollectionRequest request, DatabaseConnection cn)
        {
            var collection = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetBatchCollection(request, WebOdinContext, cn);
            return collection;
        }
    }
}