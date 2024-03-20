using System.Net;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;

namespace Odin.Services.Peer.Outgoing.Jobs;

public static class OutboxProcessingUtils
{
    public static (PeerResponseCode peerCode, TransferResult transferResult) MapPeerResponseCode(ApiResponse<PeerTransferResponse> response)
    {
        //TODO: needs more work to bring clarity to response code

        if (response.IsSuccessStatusCode)
        {
            return (response!.Content!.Code, TransferResult.Success);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (PeerResponseCode.Unknown, TransferResult.RecipientServerReturnedAccessDenied);
        }

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            return (PeerResponseCode.Unknown, TransferResult.RecipientServerError);
        }

        return (PeerResponseCode.Unknown, TransferResult.UnknownError);
    }
}