using System;
using DotYou.Types.Messaging;
using Refit;

namespace DotYou.Types.ApiClient
{
    public interface IEmailMessageClient
    {
        private const string root_path = "/api/messages";

        [Post(root_path + "/send")]
        public ApiResponse<NoResultResponse> SendMessage(Message message);

        public ApiResponse<PagedResult<Message>> GetInboxMessage(Guid id);

        public ApiResponse<NoResultResponse> Delete(Guid id);
        
        public ApiResponse<NoResultResponse> SaveDraft(Message message);
    }
}