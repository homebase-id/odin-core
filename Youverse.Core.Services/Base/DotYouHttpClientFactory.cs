using System;
using System.Net.Http;
using System.Security.Authentication;
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

        public IPerimeterHttpClient CreateClient(DotYouIdentity dotYouId)
        {
            return this.CreateClient<IPerimeterHttpClient>(dotYouId);
        }
        
        public T CreateClient<T>(DotYouIdentity dotYouId)
        {
            Console.WriteLine("CreateClient -> Loading certificate");
            var cert = _context.TenantCertificate.LoadCertificateWithPrivateKey();

            if (null == cert)
            {
                throw new Exception($"No certificate configured for {dotYouId}");
            }

            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(cert);
            handler.AllowAutoRedirect = false;
            //handler.ServerCertificateCustomValidationCallback
            handler.SslProtocols = SslProtocols.None;// | SslProtocols.Tls13;
            
            var client = new System.Net.Http.HttpClient(handler);
            client.BaseAddress = new UriBuilder() {Scheme = "https", Host = dotYouId}.Uri;

            var ogClient = RestService.For<T>(client);

            return ogClient;
        }
    }
}