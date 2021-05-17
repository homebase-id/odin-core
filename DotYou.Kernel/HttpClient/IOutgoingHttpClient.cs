using System.Threading.Tasks;
using DotYou.Types;
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
        Task<ApiResponse<NoResultResponse>> SendEmail(Message message);

        [Post(RootPath + "/invitations/connect")]
        Task<ApiResponse<NoResultResponse>> SendConnectionRequest([Body]ConnectionRequest request);
        
        [Post(RootPath + "/invitations/establishconnection")]
        Task<ApiResponse<NoResultResponse>> EstablishConnection([Body]EstablishConnectionRequest request);
    }
}