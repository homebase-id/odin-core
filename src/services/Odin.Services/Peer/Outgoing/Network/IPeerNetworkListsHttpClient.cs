using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Refit;

namespace Odin.Services.Peer.Outgoing.Network;

/// <summary>
/// Reads a connected identity's own lists: who it is connected to and who it follows. The remote answers with its
/// own permission checks, so a list it doesn't share with its connections comes back 403.
/// </summary>
public interface IPeerNetworkListsHttpClient
{
    private const string Root = PeerApiPathConstants.NetworkV1;

    [Get(Root + "/connected")]
    Task<ApiResponse<CursoredResult<string>>> GetConnected(int count, string cursor, CancellationToken cancellationToken = default);

    [Get(Root + "/identities-i-follow")]
    Task<ApiResponse<CursoredResult<string>>> GetIdentitiesIFollow(int count, string cursor,
        CancellationToken cancellationToken = default);
}
