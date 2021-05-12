using System;
using System.Threading.Tasks;
using Refit;

namespace DotYou.Types.ApiClient
{
    public interface IContactManagementClient
    {
        private const string root_path = "/api/contacts";


        [Get(root_path + "/{id}")]
        Task<ApiResponse<Contact>> GetContact(Guid id);

        [Get(root_path + "/{domainName}")]
        Task<ApiResponse<Contact>> GetContactByDomain(string domainName);

        [Get(root_path)]
        Task<ApiResponse<PagedResult<Contact>>> GetContactsList(PageOptions pageRequest);

        [Post(root_path)]
        Task<ApiResponse<NoResultResponse>> SaveContact([Body]Contact contact);

        [Get(root_path)]
        Task<ApiResponse<PagedResult<Contact>>> Find(string text, [Query]PageOptions pageRequest);

        [Delete(root_path + "/{id}")]
        Task<ApiResponse<NoResultResponse>> DeleteContact(Guid id);
    }
}
