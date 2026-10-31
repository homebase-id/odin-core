using System;
using System.Net;
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
        odinContext.PermissionsContext.AssertCanUsePeer(PeerAccess.Read);
        var (_, client) = await CreateHttpClientAsync<IPeerCircleShareKeyHttpClient>(odinId, odinContext);

        var result = await ExecuteRequestAsync(() => client.GetMySealedShareKey(circleId.Value, cancellationToken),
            cancellationToken);

        return result.IssueType switch
        {
            PeerRequestIssueType.None => result.Response.Content?.SealedKey,
            PeerRequestIssueType.Forbidden => throw new OdinSecurityException($"{odinId}: not a member of circle {circleId}"),
            PeerRequestIssueType.ForbiddenWithInvalidRemoteIcr => throw new OdinSecurityException(
                $"{odinId} no longer recognises this connection"),
            // A timeout surfaces as a cancellation too; only the caller going away is one
            PeerRequestIssueType.OperationCancelled when cancellationToken.IsCancellationRequested =>
                throw new OperationCanceledException(cancellationToken),
            PeerRequestIssueType.ServiceUnavailable or PeerRequestIssueType.SocketError or PeerRequestIssueType.HttpRequestFailed
                or PeerRequestIssueType.OperationCancelled =>
                throw new OdinClientException($"{odinId} is unavailable", OdinClientErrorCode.RemoteServerReturnedUnavailable),
            // No copy is an empty answer, so a 404 is a server that doesn't serve the route (an older one)
            _ when result.Response?.StatusCode == HttpStatusCode.NotFound =>
                throw new OdinClientException($"{odinId} doesn't serve circle share keys", OdinClientErrorCode.RemoteServerReturnedNotFound),
            _ => throw new OdinClientException(
                $"{odinId} answered {(int?)result.Response?.StatusCode} ({result.IssueType}) for circle {circleId}",
                OdinClientErrorCode.RemoteServerReturnedInternalServerError)
        };
    }
}
