using System;
using System.Threading.Tasks;
using DotYou.Types.Circle;
using DotYou.Types.Messaging;
using Refit;
using Youverse.Core;

namespace DotYou.Types.ApiClient
{
    public interface IEmailMessageClient
    {
        private const string root_path = "/api/messages";

        [Post(root_path + "/send")]
        public Task<ApiResponse<NoResultResponse>> SendMessage([Body]Message message);

        [Get(root_path + "/folder")]
        public Task<ApiResponse<PagedResult<Message>>> GetMessageList([Query]string folder, [Query]PageOptions options);

        [Get(root_path + "/{id}")]
        public Task<ApiResponse<PagedResult<Message>>> GetInboxMessage(Guid id);

        [Delete((root_path + "/{id}"))]
        public Task<ApiResponse<NoResultResponse>> Delete(Guid id);
        
        [Post(root_path)]
        public Task<ApiResponse<NoResultResponse>> Save([Body]Message message);
    }
}