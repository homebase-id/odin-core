using System;
using System.Net.Http;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using DotYou.Types.Circle;
using DotYou.Types.Messaging;
using Refit;

namespace DotYou.Kernel.HttpClient
{
    /// <summary>
    /// Sends outgoing invitations, email messages, and chat messages to other Digital Identities
    /// </summary>
    public interface IOutgoingHttpClient
    {
        private const string RootPath = "/api/incoming";

        [Post(RootPath + "/email")]
        Task<ApiResponse<bool>> SendEmail(Message message);

        [Post(RootPath + "/invitations/connect")]
        Task<ApiResponse<bool>> SendConnectionRequest(ConnectionRequest request);
        
        [Post(RootPath + "/invitations/establishconnection")]
        Task<ApiResponse<bool>> EstablishConnection(EstablishConnectionRequest request);
    }

    public class HttpClientFactory
    {
        private DotYouContext _context;

        public HttpClientFactory(DotYouContext context)
        {
            _context = context;
        }

        public IOutgoingHttpClient CreateClient(DotYouIdentity dotYouId)
        {
            var cert = _context.TenantCertificate.LoadCertificateWithPrivateKey();

            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(cert);
            handler.AllowAutoRedirect = false;
            //handler.ServerCertificateCustomValidationCallback

            var client = new System.Net.Http.HttpClient(handler);
            client.BaseAddress = new UriBuilder() {Scheme = "https", Host = dotYouId}.Uri;
            
            var ogClient = RestService.For<IOutgoingHttpClient>(client);

            return ogClient;
        }
    }
}