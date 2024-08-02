using System.Threading.Tasks;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Reactions
{
    public interface IPeerGroupReactionHttpClient
    {
        private const string RootPath = PeerApiPathConstants.ReactionsV1;

        [Post(RootPath + "/add")]
        Task<ApiResponse<PeerResponseCode>> AddReaction([Body] RemoteReactionRequestRedux payload);

        [Post(RootPath + "/delete")]
        Task<ApiResponse<PeerResponseCode>> DeleteReaction([Body] RemoteReactionRequestRedux payload);
    }
}