using System.Threading.Tasks;
using Refit;

namespace Youverse.Hosting.Tests.ApiClient
{
    public interface IDemoDataClient
    {
        private const string RootPath = "/api/demodata";
            
        [Get(RootPath+ "/contacts")]
        Task<ApiResponse<bool>> ImportContacts();
    }
}