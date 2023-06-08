using System.Net.Http;
using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Registry
{
    public interface ICertificateStatusHttpClient
    {
        private const string Root = "/api/owner/v1/config/certificate";
        
        [Get(Root + "/verifyCertificatesValid")]
        Task<ApiResponse<bool>> VerifyCertificatesValid();
    }
}