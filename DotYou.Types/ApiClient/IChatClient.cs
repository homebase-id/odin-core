using System.Threading.Tasks;
using DotYou.Types.Admin;
using DotYou.Types.Messaging;
using Refit;

namespace DotYou.Types.ApiClient
{
    public interface IChatClient
    {
        private const string root_path = "/api/messages/chat";

        [Post(root_path + "/send")]
        public Task<ApiResponse<NoResultResponse>> SendMessage([Body]ChatMessageEnvelope message);

        [Get(root_path + "/availablecontacts")]
        public Task<ApiResponse<PagedResult<AvailabilityStatus>>> GetAvailableContacts([Query] PageOptions pageRequest);
        
    }
}