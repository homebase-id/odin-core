using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotYou.Types.TrustNetwork;
using Refit;

namespace DotYou.Types
{
    public interface IContactRequestsClient
    {
        private const string root_path = "/api/contacts";


        [Get(root_path + "/{id}")]
        Task<ApiResponse<Contact>> GetContact(Guid id);

        [Get(root_path + "/{domainName}")]
        Task<ApiResponse<Contact>> GetContact(string domainName);

        [Get(root_path)]
        Task<ApiResponse<PagedResult<Contact>>> GetContactsList(PageOptions pageRequest);

        [Post(root_path + "/{id}")]
        Task<ApiResponse<NoResultResponse>> SaveContact(Contact contact);

        [Get(root_path)]
        Task<ApiResponse<PagedResult<Contact>>> Find(string text, [Query]PageOptions pageRequest);

        [Delete(root_path + "/{id}")]
        Task<ApiResponse<NoResultResponse>> DeleteContact(Guid id);
    }
}
