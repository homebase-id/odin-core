using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;

namespace Odin.Services.Peer.Outgoing.Network;

/// <summary>
/// Reads this identity's own sealed copy of a connected identity's circle key (Selective Share).  The remote
/// answers only for a circle that currently grants this identity, and only with this identity's copy.
/// </summary>
public interface IPeerCircleShareKeyHttpClient
{
    private const string Root = PeerApiPathConstants.CirclesV1;

    [Get(Root + "/{circleId}/my-share-key")]
    Task<ApiResponse<SealedShareKeyResponse>> GetMySealedShareKey(Guid circleId, CancellationToken cancellationToken);
}

/// <summary>A caller's sealed copy of a circle's share key, as the circle's owner stored it; null while there is none.</summary>
public class SealedShareKeyResponse
{
    public byte[] SealedKey { get; set; }
}
