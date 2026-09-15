using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.UnifiedV2.Connections;
using Odin.Services.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class V2ConnectionNetworkClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<List<CircleWithMembers>>> GetCirclesWithMembersAsync(bool includeSystemCircle = true)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IConnectionNetworkHttpClientApiV2>(client, sharedSecret);
        return await svc.GetCirclesWithMembers(includeSystemCircle);
    }

    public async Task<ApiResponse<HttpContent>> BlockAsync(OdinId odinId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IConnectionNetworkHttpClientApiV2>(client, sharedSecret);
        return await svc.Block(new OdinIdRequest { OdinId = odinId });
    }

    public async Task<ApiResponse<HttpContent>> UnblockAsync(OdinId odinId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IConnectionNetworkHttpClientApiV2>(client, sharedSecret);
        return await svc.Unblock(new OdinIdRequest { OdinId = odinId });
    }

    public async Task<ApiResponse<HttpContent>> DisconnectAsync(OdinId odinId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IConnectionNetworkHttpClientApiV2>(client, sharedSecret);
        return await svc.Disconnect(new OdinIdRequest { OdinId = odinId });
    }

    public async Task<ApiResponse<List<OdinId>>> GetCircleMembersAsync(Guid circleId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IConnectionNetworkHttpClientApiV2>(client, sharedSecret);
        return await svc.GetCircleMembers(circleId);
    }

    public async Task<ApiResponse<RedactedIdentityConnectionRegistration>> GetConnectionInfoAsync(OdinId odinId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IConnectionNetworkHttpClientApiV2>(client, sharedSecret);
        return await svc.GetConnectionInfo(odinId.ToString());
    }

    public async Task<ApiResponse<HttpContent>> GrantCircleAsync(Guid circleId, OdinId odinId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IConnectionNetworkHttpClientApiV2>(client, sharedSecret);
        return await svc.GrantCircle(new AddCircleMembershipRequest { CircleId = circleId, OdinId = odinId });
    }

    public async Task<ApiResponse<HttpContent>> RevokeCircleAsync(Guid circleId, OdinId odinId)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IConnectionNetworkHttpClientApiV2>(client, sharedSecret);
        return await svc.RevokeCircle(new RevokeCircleMembershipRequest { CircleId = circleId, OdinId = odinId });
    }
}
