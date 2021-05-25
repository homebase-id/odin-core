using System;
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

        [Get(root_path + "/history")]
        public Task<ApiResponse<DateRangePagedResult<ChatMessageEnvelope>>> GetHistory([Query] DotYouIdentity dotYouId, Int64 startDateTimeOffsetSeconds, Int64 endDateTimeOffsetSeconds, int pageNumber, int pageSize);
        
        [Get(root_path + "/availablecontacts")]
        public Task<ApiResponse<PagedResult<AvailabilityStatus>>> GetAvailableContacts([Query] PageOptions pageRequest);
        
    }
}