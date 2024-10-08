﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Apps;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.Controllers.OwnerToken.Transit;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Transit
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IRefitOwnerTransitQuery
    {
        private const string RootEndpoint = OwnerApiPathConstants.PeerQueryV1;

        // [Post(RootQueryEndpoint + "/modified")]
        // Task<ApiResponse<QueryModifiedResult>> GetModified(QueryModifiedRequest request);

        [Post(RootEndpoint + "/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch([Body] PeerQueryBatchRequest request);

        [Post(RootEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader([Body] TransitExternalFileIdentifier file);

        [Post(RootEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayload([Body] TransitGetPayloadRequest file);

        [Post(RootEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail([Body] TransitGetThumbRequest request);
        
        [Post(RootEndpoint + "/metadata/type")]
        Task<ApiResponse<PagedResult<ClientDriveData>>> GetDrives([Body] TransitGetDrivesByTypeRequest request);
    }
}