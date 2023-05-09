using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Certificate.Renewal;

namespace Youverse.Core.Services.Registry
{
    public interface ICertificateStatusHttpClient
    {
        private const string Root = "/api/owner/v1/config/certificate";
        
        
        [Post(Root + "/initializecertificate")]
        Task<ApiResponse<HttpContent>> InitializeCertificate();
        
        [Post(Root + "/ensurevalidcertificate")]
        Task<ApiResponse<HttpContent>> EnsureValidCertificates();

        // [Post(Root + "/generatecertificate")]
        // Task<ApiResponse<CertificateOrderStatus>> CheckCertificateCreationStatus();
        
        [Get(Root + "/verifyCertificatesValid")]
        Task<ApiResponse<bool>> VerifyCertificatesValid();
    }
}