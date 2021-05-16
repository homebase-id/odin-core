using System.Threading.Tasks;
using DotYou.Types.Circle;
using DotYou.Types.Messaging;
using Refit;

namespace DotYou.Kernel.HttpClient
{
    /// <summary>
    /// Sends outgoing invitations, email messages, and chat messages to other Digital Identities
    /// </summary>
    public interface IOutgoingHttpClient
    {
        private const string RootPath = "/api/incoming";

        [Post(RootPath + "/email")]
        Task<ApiResponse<bool>> SendEmail(Message message);

        [Post(RootPath + "/invitations/connect")]
        Task<ApiResponse<bool>> SendConnectionRequest(ConnectionRequest request);
        
        [Post(RootPath + "/invitations/establishconnection")]
        Task<ApiResponse<bool>> EstablishConnection(EstablishConnectionRequest request);
    }
}