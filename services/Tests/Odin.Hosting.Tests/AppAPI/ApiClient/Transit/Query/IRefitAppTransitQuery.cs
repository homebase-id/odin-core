﻿using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Query
{
    public interface IRefitAppTransitQuery
    {
        private const string RootEndpoint = AppApiPathConstants.TransitQueryV1;

        [Post(RootEndpoint + "/modified")]
        Task<ApiResponse<QueryModifiedResponse>> GetModified(TransitQueryModifiedRequest request);

        [Post(RootEndpoint + "/batchcollection")]
        Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection([Body] TransitQueryBatchCollectionRequest request);
        
        [Post(RootEndpoint + "/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch([Body] TransitQueryBatchRequest request);

        [Post(RootEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader([Body] TransitExternalFileIdentifier file);

        [Post(RootEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayload([Body] TransitExternalFileIdentifier file);

        [Post(RootEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail([Body] TransitGetThumbRequest request);
        
        [Post(RootEndpoint + "/metadata/type")]
        Task<ApiResponse<PagedResult<ClientDriveData>>> GetDrives([Body] TransitGetDrivesByTypeRequest request);
    }
}