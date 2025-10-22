﻿using System.Threading.Tasks;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive
{
    /// <summary>
    /// Base class for querying a drive's search index
    /// </summary>
    public abstract class DriveQueryControllerBase : OdinControllerBase
    {
        protected async Task<QueryBatchResponse> QueryBatch(QueryBatchRequest request)
        {
            var driveId = request.QueryParams.TargetDrive.Alias;
            var batch = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions(), WebOdinContext);
            return QueryBatchResponse.FromResult(batch);
        }
        
        protected async Task<QueryBatchResponse> QuerySmartBatch(QueryBatchRequest request)
        {
            var driveId = request.QueryParams.TargetDrive.Alias;
            var batch = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetSmartBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions(), WebOdinContext);
            return QueryBatchResponse.FromResult(batch);
        }

        protected async Task<QueryBatchCollectionResponse> QueryBatchCollection(QueryBatchCollectionRequest request)    
        {
            var collection = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetBatchCollection(request, WebOdinContext);
            return collection;
        }
    }
}