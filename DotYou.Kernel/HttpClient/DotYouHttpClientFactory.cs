using System;
using System.Net.Http;
using DotYou.IdentityRegistry;
using DotYou.Types;
using Refit;

namespace DotYou.Kernel.HttpClient
{
    /// <summary>
    /// Creates clients for http requests to other digital identity servers
    /// </summary>
    public class DotYouHttpClientFactory
    {
        private DotYouContext _context;

        public DotYouHttpClientFactory(DotYouContext context)
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