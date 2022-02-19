using System;
using System.Net.Http;
using System.Security.Authentication;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Creates clients for http requests to other digital identity servers
    /// </summary>
    public class DotYouHttpClientFactory : IDotYouHttpClientFactory
    {
        private readonly DotYouContext _context;
        private readonly ICertificateResolver _certificateResolver;

        public DotYouHttpClientFactory(DotYouContext context, ICertificateResolver certificateResolver)
        {
            _context = context;
            _certificateResolver = certificateResolver;
        }

        public IPerimeterHttpClient CreateClient(DotYouIdentity dotYouId)
        {
            return this.CreateClient<IPerimeterHttpClient>(dotYouId);
        }

        public T CreateClient<T>(DotYouIdentity dotYouId, Guid? appIdOverride = null)
        {
            //HACK: this appIdOverride is strange but required so the background sender
            //can specify the app since it doesnt know
            Console.WriteLine("CreateClient -> Loading certificate");
            var cert = _certificateResolver.GetSslCertificate();

            if (null == cert)
            {
                throw new Exception($"No certificate configured for {dotYouId}");
            }

            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(cert);
            handler.AllowAutoRedirect = false;
            //handler.ServerCertificateCustomValidationCallback
            handler.SslProtocols = SslProtocols.None; // | SslProtocols.Tls13;

            var client = new HttpClient(handler)
            {
                BaseAddress = new UriBuilder()
                {
                    Scheme = "https",
                    Host = dotYouId
                }.Uri
            };

            var appId = appIdOverride.HasValue ? appIdOverride.ToString() : _context.AppContext?.AppId.ToString() ?? "";
            client.DefaultRequestHeaders.Add(DotYouHeaderNames.AppId, appId);

            var ogClient = RestService.For<T>(client);

            return ogClient;
        }
    }
}