using System.Threading.Tasks;
using Refit;

namespace DotYou.Types.ApiClient
{
    public interface IDemoDataClient
    {
        private const string RootPath = "/api/demodata";
            
        [Get(RootPath+ "/contacts")]
        Task<ApiResponse<bool>> ImportContacts();
    }
}