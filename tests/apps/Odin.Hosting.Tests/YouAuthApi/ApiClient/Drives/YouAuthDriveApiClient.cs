using System;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Apps;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer;
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
            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            var batch = response.Content;
            ClassicAssert.IsNotNull(batch);

            return batch;
        }
    }
    
    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(ExternalFileIdentifier file, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = CreateYouAuthApiHttpClient(_token, fileSystemType);
        {
            //wth - refit is not sending headers when you do GET request - why not!?
            var svc = CreateDriveService(client);
            // var apiResponse = await svc.GetFileHeader(file.FileId, file.TargetDrive.Alias, file.TargetDrive.Type);
            var apiResponse = await svc.GetFileHeader(file);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnail(ExternalFileIdentifier file, int width, int height,string payloadKey, bool directMatchOnly = false,
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
                PayloadKey = payloadKey,
                DirectMatchOnly = directMatchOnly
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
            
        client.BaseAddress = new Uri($"https://{_identity.OdinId}:{WebScaffold.HttpsPort}");
        return client;    }

    private IRefitGuestDriveQuery CreateDriveService(HttpClient client)
    {
        return RefitCreator.RestServiceFor<IRefitGuestDriveQuery>(client, _token.SharedSecret);
    }
    
}