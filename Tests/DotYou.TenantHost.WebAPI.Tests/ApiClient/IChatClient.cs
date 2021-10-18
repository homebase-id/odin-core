using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services;
using Youverse.Core.Services.Identity;
using Youverse.Services.Messaging;

namespace DotYou.Types.ApiClient
{
    public interface IChatClient
    {
        private const string root_path = "/api/messages/chat";

        [Post(root_path + "/send")]
        public Task<ApiResponse<NoResultResponse>> SendMessage([Body]ChatMessageEnvelope message);

        [Get(root_path + "/history")]
        public Task<ApiResponse<DateRangePagedResult<ChatMessageEnvelope>>> GetHistory([Query] string dotYouId, Int64 startDateTimeOffsetSeconds, Int64 endDateTimeOffsetSeconds, int pageNumber, int pageSize);
        
        [Get(root_path + "/availablecontacts")]
        public Task<ApiResponse<PagedResult<AvailabilityStatus>>> GetAvailableContacts([Query] PageOptions pageRequest);
        
    }
}