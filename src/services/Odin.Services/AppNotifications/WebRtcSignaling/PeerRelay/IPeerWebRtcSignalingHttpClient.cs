using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.AppNotifications.WebRtcSignaling.PeerRelay;

public interface IPeerWebRtcSignalingHttpClient
{
    private const string RootPath = PeerApiPathConstants.WebRtcV1;

    [Post(RootPath + "/relay")]
    Task<ApiResponse<WebRtcRelayResponse>> Relay([Body] WebRtcRelayRequest request,
        CancellationToken cancellationToken = default);
}
