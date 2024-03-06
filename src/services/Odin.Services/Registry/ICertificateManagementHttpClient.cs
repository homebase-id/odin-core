using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Refit;

namespace Odin.Services.Registry
{
    public interface ICertificateStatusHttpClient
    {
        private const string Root = $"{OwnerApiPathConstants.ConfigurationV1}/certificate";
        
        [Get(Root + "/verifyCertificatesValid")]
        Task<ApiResponse<bool>> VerifyCertificatesValid();
    }
}