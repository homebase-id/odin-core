using System;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Transit;
using Odin.Core.Storage;
using Odin.Hosting.Tests.AppAPI.Drive;
using Refit;

namespace Odin.Hosting.Tests.YouAuthApi.ApiClient.Drives;

public class YouAuthDriveApiClient
{
    private readonly TestIdentity _identity;
    private readonly ClientAccessToken _token;

    public YouAuthDriveApiClient(TestIdentity identity, ClientAccessToken token)
    {
        _identity = identity;
        _token = token;
    }

    public async Task<QueryBatchResponse> QueryBatch(FileSystemType fileSystemType, FileQueryParams qp, QueryBatchResultOptionsRequest resultOptions = null)
    {
        var client = CreateYouAuthApiHttpClient(_token, fileSystemType);
        {
            var svc = CreateDriveService(client);

            var ro = resultOptions ?? new QueryBatchResultOptionsRequest()
            {
                CursorState = "",
                MaxRecords = 10,
                IncludeMetadataHeader = true
            };

            var request = new QueryBatchRequest()
            {
                QueryParams = qp,
                ResultOptionsRequest = ro
            };

            var response = await svc.GetBatch(request);
            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            var batch = response.Content;
            Assert.IsNotNull(batch);

            return batch;
        }
    }
    
    public async Task<SharedSecretEncryptedFileHeader> GetFileHeader(ExternalFileIdentifier file, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = CreateYouAuthApiHttpClient(_token, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = CreateDriveService(client);
            // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
            var apiResponse = await svc.GetFileHeader(file);
            return apiResponse.Content;
        }
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnail(ExternalFileIdentifier file, int width, int height, bool directMatchOnly = false,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = CreateYouAuthApiHttpClient(_token, fileSystemType);
        {
            var sharedSecret = _token.SharedSecret;
            var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, sharedSecret);

            var thumbnailResponse = await driveSvc.GetThumbnailAsPost(new GetThumbnailRequest()
            {
                File = file,
                Height = height,
                Width = width,
                DirectMatchOnly = directMatchOnly
            });

            return thumbnailResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> GetPayload(ExternalFileIdentifier file, FileChunk chunk = null,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = CreateYouAuthApiHttpClient(_token, fileSystemType);
        {
            var sharedSecret = _token.SharedSecret;
            var driveSvc = RefitCreator.RestServiceFor<IDriveTestHttpClientForApps>(client, sharedSecret);

            var thumbnailResponse = await driveSvc.GetPayloadAsPost(new GetPayloadRequest()
            {
                File = file,
                Chunk = chunk
            });

            return thumbnailResponse;
        }
    }


    private HttpClient CreateYouAuthApiHttpClient(ClientAccessToken token, FileSystemType fileSystemType)
    {
        // var client = WebScaffold.CreateHttpClient<YouAuthDriveApiClient>();
        HttpClient client = new();
        //
        // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
        // DO NOT do this in production code!
        //
        {
            var cookieValue = $"{YouAuthDefaults.XTokenCookieName}={token.ToAuthenticationToken()}";
            client.DefaultRequestHeaders.Add("Cookie", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(token.SharedSecret.GetKey()));
        }
            
        client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
        client.Timeout = TimeSpan.FromMinutes(15);
            
        client.BaseAddress = new Uri($"https://{this._identity}");
        return client;    }

    private IDriveTestHttpClientForYouAuth CreateDriveService(HttpClient client)
    {
        return RefitCreator.RestServiceFor<IDriveTestHttpClientForYouAuth>(client, _token.SharedSecret);
    }
    
}