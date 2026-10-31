using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Peer.Outgoing.Network;

/// <summary>
/// Fetches this identity's sealed copy of a connected identity's circle key from their server (Selective Share,
/// docs/selective-share-plan.md).  The remote answers only while the circle grants this identity.
/// </summary>
public class PeerCircleShareKeyService(
    IOdinHttpClientFactory odinHttpClientFactory,
    CircleNetworkService circleNetworkService,
    FileSystemResolver fileSystemResolver,
    OdinConfiguration odinConfiguration)
    : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
{
    /// <summary>Null when they have sealed no copy for this identity yet.</summary>
    public async Task<byte[]> GetMySealedShareKeyAsync(OdinId odinId, GuidId circleId, IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        var token = await ResolveClientAccessTokenAsync(odinId, odinContext, PeerAccess.Read);
        var client = await OdinHttpClientFactory.CreateClientUsingAccessTokenAsync<IPeerCircleShareKeyHttpClient>(
            odinId, token.ToAuthenticationToken());

        var result = await ExecuteRequestAsync(() => client.GetMySealedShareKey(circleId.Value, cancellationToken),
            cancellationToken);

        return result.IssueType switch
        {
            // An empty answer is "none yet"; a 404 would be a server that doesn't serve the route
            PeerRequestIssueType.None => result.Response.Content?.SealedKey,
            PeerRequestIssueType.Forbidden => throw new OdinSecurityException($"{odinId}: not a member of circle {circleId}"),
            PeerRequestIssueType.ForbiddenWithInvalidRemoteIcr => throw new OdinSecurityException(
                $"{odinId} no longer recognises this connection"),
            PeerRequestIssueType.ServiceUnavailable or PeerRequestIssueType.SocketError or PeerRequestIssueType.HttpRequestFailed =>
                throw new OdinClientException($"{odinId} is unavailable", OdinClientErrorCode.RemoteServerReturnedUnavailable),
            PeerRequestIssueType.OperationCancelled => throw new OperationCanceledException(cancellationToken),
            _ => throw new OdinClientException(
                $"{odinId} answered {(int?)result.Response?.StatusCode} ({result.IssueType}) for circle {circleId}",
                OdinClientErrorCode.RemoteServerReturnedInternalServerError)
        };
    }
}
