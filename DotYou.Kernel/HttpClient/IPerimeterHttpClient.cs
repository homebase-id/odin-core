using System.Threading.Tasks;
using DotYou.Types;
using DotYou.Types.Admin;
using DotYou.Types.Circle;
using DotYou.Types.Messaging;
using Refit;

namespace DotYou.Kernel.HttpClient
{
    /// <summary>
    /// Sends outgoing invitations, email messages, and chat messages to other Digital Identities
    /// </summary>
    public interface IPerimeterHttpClient
    {
        private const string RootPath = "/api/perimeter";

        [Post(RootPath + "/email")]
        Task<ApiResponse<NoResultResponse>> DeliverEmail(Message message);

        [Post(RootPath + "/invitations/connect")]
        Task<ApiResponse<NoResultResponse>> DeliverConnectionRequest([Body]ConnectionRequest request);
        
        [Post(RootPath + "/invitations/establishconnection")]
        Task<ApiResponse<NoResultResponse>> EstablishConnection([Body]EstablishConnectionRequest request);

        [Post(RootPath + "/chat")]
        Task<ApiResponse<NoResultResponse>> DeliverChatMessage(ChatMessageEnvelope message);
        
        [Post(RootPath + "/status/chat")]
        Task<ApiResponse<bool>> GetAvailability();

        [Get(RootPath + "/publicinfo/profile")]
        Task<ApiResponse<PublicProfile>> GetPublicProfile();
    }
}