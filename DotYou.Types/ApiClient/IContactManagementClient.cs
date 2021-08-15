using System;
using System.Threading.Tasks;
using DotYou.Types.Messaging;
using Refit;

namespace DotYou.Types.ApiClient
{
    public interface IContactManagementClient
    {
        private const string root_path = "/api/contacts";


        [Get(root_path + "/{id}")]
        Task<ApiResponse<DotYouProfile>> GetContact(Guid id);

        [Get(root_path + "/{domainName}")]
        Task<ApiResponse<DotYouProfile>> GetContactByDomain(string domainName);

        [Get(root_path)]
        Task<ApiResponse<PagedResult<DotYouProfile>>> GetContactsList(PageOptions pageRequest, bool connectedContactsOnly);

        [Post(root_path)]
        Task<ApiResponse<NoResultResponse>> SaveContact([Body]DotYouProfile dotYouProfile);

        [Get(root_path + "/find")]
        Task<ApiResponse<PagedResult<DotYouProfile>>> Find(string text, [Query]PageOptions pageRequest);

        [Delete(root_path + "/{id}")]
        Task<ApiResponse<NoResultResponse>> DeleteContact(Guid id);
    }
}
