using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Core.Storage;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Transit.Query;

public class OwnerTransitQueryApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public OwnerTransitQueryApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }
   
    
    public async Task<ApiResponse<RedactedOdinContext>> GetRemoteDotYouContext(TransitGetSecurityContextRequest request)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerTransitQuery>(client, sharedSecret);
            var apiResponse = await svc.GetRemoteDotYouContext(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection(PeerQueryBatchCollectionRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerTransitQuery>(client, sharedSecret);
            var apiResponse = await svc.GetBatchCollection(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<QueryModifiedResponse>> GetModified(PeerQueryModifiedRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerTransitQuery>(client, sharedSecret);
            var apiResponse = await svc.GetModified(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<QueryBatchResponse>> GetBatch(PeerQueryBatchRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerTransitQuery>(client, sharedSecret);
            var apiResponse = await svc.GetBatch(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(TransitExternalFileIdentifier file,
        FileSystemType fst = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerTransitQuery>(client, sharedSecret);
            var apiResponse = await svc.GetFileHeader(file);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> GetPayload(PeerGetPayloadRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerTransitQuery>(client, sharedSecret);
            var apiResponse = await svc.GetPayload(request);
            return apiResponse;
        }
    }
    
    public async Task<ApiResponse<HttpContent>> GetThumbnail(TransitGetThumbRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerTransitQuery>(client, sharedSecret);
            var apiResponse = await svc.GetThumbnail(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<PagedResult<ClientDriveData>>> GetDrives(TransitGetDrivesByTypeRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var sharedSecret, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerTransitQuery>(client, sharedSecret);
            var apiResponse = await svc.GetDrives(request);
            return apiResponse;
        }
    }
}