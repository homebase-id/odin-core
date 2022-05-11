using System;
using System.Net.Http;
using System.Security.Authentication;
using Dawn;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Creates clients for http requests to other digital identity servers
    /// </summary>
    public class DotYouHttpClientFactory : IDotYouHttpClientFactory
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ICertificateResolver _certificateResolver;

        public DotYouHttpClientFactory(DotYouContextAccessor contextAccessor, ICertificateResolver certificateResolver)
        {
            _contextAccessor = contextAccessor;
            _certificateResolver = certificateResolver;
        }

        public T CreateClientUsingAccessToken<T>(DotYouIdentity dotYouId, ClientAuthenticationToken clientAuthenticationToken, Guid? appIdOverride = null)
        {
            Guard.Argument(clientAuthenticationToken, nameof(clientAuthenticationToken)).NotNull();
            Guard.Argument(clientAuthenticationToken.Id, nameof(clientAuthenticationToken.Id)).Require(x => x != Guid.Empty);
            Guard.Argument(clientAuthenticationToken.AccessTokenHalfKey, nameof(clientAuthenticationToken.AccessTokenHalfKey)).Require(x => x.IsSet());
            Guard.Argument(clientAuthenticationToken, nameof(clientAuthenticationToken)).NotNull();

            return this.CreateClientInternal<T>(dotYouId, clientAuthenticationToken, appIdOverride);
        }

        public T CreateClient<T>(DotYouIdentity dotYouId, Guid? appIdOverride = null)
        {

            return this.CreateClientInternal<T>(dotYouId, null, appIdOverride);
        }

        ///
        
        private T CreateClientInternal<T>(DotYouIdentity dotYouId, ClientAuthenticationToken clientAuthenticationToken, Guid? appIdOverride = null)
        {
            Guard.Argument(dotYouId.Id, nameof(dotYouId)).NotNull();
            
            var appId = appIdOverride.HasValue ? appIdOverride.ToString() : _contextAccessor.GetCurrent().AppContext?.AppId.ToString() ?? "";
            Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();

            var handler = new HttpClientHandler();

            //HACK: this appIdOverride is strange but required so the background sender
            //can specify the app since it doesnt know
            // Console.WriteLine("CreateClient -> Loading certificate");
            var cert = _certificateResolver.GetSslCertificate();
            if (null == cert)
            {
                throw new Exception($"No certificate configured for {dotYouId}");
            }

            handler.ClientCertificates.Add(cert);
            handler.AllowAutoRedirect = false; //we should go directly to the endpoint; nothing in between
            handler.SslProtocols = SslProtocols.None; //allow OS to choose;
            //handler.ServerCertificateCustomValidationCallback

            var client = new HttpClient(handler)
            {
                BaseAddress = new UriBuilder()
                {
                    Scheme = "https",
                    Host = dotYouId
                }.Uri
            };

            client.DefaultRequestHeaders.Add(DotYouHeaderNames.AppId, appId);
            if (null != clientAuthenticationToken)
            {
                //TODO: need to encrypt this token somehow? (shared secret?)
                client.DefaultRequestHeaders.Add(DotYouHeaderNames.ClientAuthToken, clientAuthenticationToken.ToString());
            }

            var ogClient = RestService.For<T>(client);
            return ogClient;
        }
    }
}