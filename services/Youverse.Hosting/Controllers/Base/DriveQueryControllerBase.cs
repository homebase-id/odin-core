using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Controllers.Base
{
    /// <summary>
    /// Base class for querying a drive's search index
    /// </summary>
    public abstract class DriveQueryControllerBase : OdinControllerBase
    {
        protected async Task<QueryModifiedResult> QueryModified(QueryModifiedRequest request)
        {
            var driveId = DotYouContext.PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await GetFileSystemResolver().ResolveFileSystem().Query.GetModified(driveId, request.QueryParams, request.ResultOptions);
            return batch;
        }

        protected async Task<QueryBatchResponse> QueryBatch(QueryBatchRequest request)
        {
            var driveId = DotYouContext.PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await GetFileSystemResolver().ResolveFileSystem().Query.GetBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions());
            return QueryBatchResponse.FromResult(batch);
        }

        protected async Task<QueryBatchCollectionResponse> QueryBatchCollection(QueryBatchCollectionRequest request)
        {
            var collection = await GetFileSystemResolver().ResolveFileSystem().Query.GetBatchCollection(request);
            return collection;
        }
    }
}