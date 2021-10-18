using System.IO;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Storage;

namespace Youverse.Services.Messaging
{
    /// <summary>
    /// Sends outgoing invitations, email messages, and chat messages to other Digital Identities
    /// </summary>
    public interface IMessagingPerimeterHttpClient
    {
        private const string RootPath = "/api/perimeter";

        [Post(RootPath + "/email")]
        Task<ApiResponse<NoResultResponse>> DeliverEmail(Message message);

        [Multipart]
        [Post(RootPath + "/chat")]
        Task<ApiResponse<NoResultResponse>> DeliverChatMessage(ChatMessageEnvelope envelope, MediaMetaData metaData, Stream media);

        //
        [Post(RootPath + "/status/chat")]
        Task<ApiResponse<bool>> GetAvailability();

    }
}