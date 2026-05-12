using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class DrivePeerReaderV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<FileExistsOnPeerResponse>> GetFileExistsByUidAsync(OdinId peer, Guid driveId, Guid uid)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetFileExistsByUid(peer.DomainName, driveId, uid);
    }

    public async Task<ApiResponse<FileExistsOnPeerResponse>> GetFileExistsByGtidAsync(OdinId peer, Guid driveId, Guid gtid)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDrivePeerQueryHttpClientApiV2>(client, sharedSecret);
        return await svc.GetFileExistsByGtid(peer.DomainName, driveId, gtid);
    }
}
