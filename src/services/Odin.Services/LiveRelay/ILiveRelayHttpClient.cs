using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.LiveRelay;

/// <summary>
/// Server-to-server (hop 2) client: the sender's server fires a live data point at each recipient's
/// peer perimeter, fire-and-forget. Mirrors <c>IPeerAppNotificationHttpClient</c>.
/// </summary>
public interface ILiveRelayHttpClient
{
    [Post(PeerApiPathConstants.LiveRelayV1 + "/relay")]
    Task<ApiResponse<PeerTransferResponse>> Relay([Body] LiveRelayPeerEnvelope envelope, CancellationToken cancellationToken = default);
}
