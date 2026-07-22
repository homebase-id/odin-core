#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.UnifiedV2.Connections;
using Odin.Services.Contacts;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Bundles the connection-network surface the fast-framework tests need: V2 endpoints
/// (<see cref="AutoConnectAsync"/>, <see cref="PreflightIntroductionsAsync"/>) and the V1 admin
/// helpers used for connection-request flow setup and assertion (send / accept / disconnect /
/// get-info / block / revoke-circle / delete-request). Used by Phase-3 Connections tests; not part
/// of the IV2Caller surface because only the owner ever drives these flows.
/// </summary>
public sealed class ConnectionsHandle
{
    private readonly OwnerSession _owner;
    private readonly UniversalCircleNetworkRequestsApiClient _requests;
    private readonly UniversalCircleNetworkApiClient _network;
    private readonly V2ConnectionRequestsClient _v2Requests;
    private readonly V2IntroductionsClient _v2Introductions;

    internal ConnectionsHandle(OwnerSession owner)
    {
        _owner = owner;
        _requests = new UniversalCircleNetworkRequestsApiClient(owner.Identity, owner.Factory);
        _network = new UniversalCircleNetworkApiClient(owner.Identity, owner.Factory);
        _v2Requests = new V2ConnectionRequestsClient(owner.Identity, owner.Factory);
        _v2Introductions = new V2IntroductionsClient(owner.Identity, owner.Factory);
    }

    // -----------------------------------------------------------------------------------------
    // V2 endpoints
    // -----------------------------------------------------------------------------------------

    /// <summary>POST /api/v2/connections/auto-connect — caller-driven auto-connect flow.</summary>
    public Task<ApiResponse<ConnectionRequestResult>> AutoConnectAsync(ConnectionRequestHeader header)
        => _v2Requests.AutoConnectAsync(header);

    /// <summary>POST /api/v2/introductions/preflight — recipient eligibility check before SendIntroductions.</summary>
    public Task<ApiResponse<IntroductionPreflightResult>> PreflightIntroductionsAsync(IntroductionGroup group)
        => _v2Introductions.PreflightIntroductionsAsync(group);

    /// <summary>PUT /api/v2/connections/requests/incoming/{senderId} — accept a pending incoming request.</summary>
    public Task<ApiResponse<HttpContent>> AcceptIncomingRequestV2Async(OdinId sender, AcceptConnectionRequestV2 request)
        => _v2Requests.AcceptIncomingRequestAsync(sender, request);

    // -----------------------------------------------------------------------------------------
    // V1 helpers (test setup + assertions)
    // -----------------------------------------------------------------------------------------

    public Task<ApiResponse<HttpContent>> SendConnectionRequest(OdinId recipient,
        IEnumerable<GuidId>? circlesGrantedToRecipient = null, ContactContent? contactCard = null)
        => _requests.SendConnectionRequest(recipient, circlesGrantedToRecipient, contactCard);

    public Task<ApiResponse<HttpContent>> AcceptConnectionRequest(OdinId sender, IEnumerable<GuidId>? circleIdsGrantedToSender = null)
        => _requests.AcceptConnectionRequest(sender, circleIdsGrantedToSender);

    public Task<ApiResponse<HttpContent>> DisconnectFrom(OdinId recipient)
        => _requests.DisconnectFrom(recipient);

    public Task<ApiResponse<ConnectionRequestResponse>> GetIncomingRequestFrom(OdinId sender)
        => _requests.GetIncomingRequestFrom(sender);

    public Task<ApiResponse<ConnectionRequestResponse>> GetOutgoingSentRequestTo(OdinId recipient)
        => _requests.GetOutgoingSentRequestTo(recipient);

    public Task<ApiResponse<HttpContent>> DeleteSentRequestTo(OdinId recipient)
        => _requests.DeleteSentRequestTo(recipient);

    public Task<ApiResponse<HttpContent>> DeleteConnectionRequestFrom(OdinId sender)
        => _requests.DeleteConnectionRequestFrom(sender);

    public Task<ApiResponse<RedactedIdentityConnectionRegistration>> GetConnectionInfo(OdinId recipient)
        => _network.GetConnectionInfo(recipient);

    public Task<ApiResponse<HttpContent>> BlockConnection(OdinId odinId) => _network.BlockConnection(odinId);
    public Task<ApiResponse<HttpContent>> UnblockConnection(OdinId odinId) => _network.UnblockConnection(odinId);
    public Task<ApiResponse<HttpContent>> RevokeCircle(Guid circleId, OdinId odinId) => _network.RevokeCircle(circleId, odinId);
}
