using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class DrivePeerReaderV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<bool>> GetFileExistsAsync(Guid driveId, PeerFileExistsByUidAndVersionTagRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetFileExists(driveId, request);
    }
}
