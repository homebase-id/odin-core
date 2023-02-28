using System;
using System.Net.Http;
using System.Security.Authentication;
using Dawn;
using Refit;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Certificate;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Creates clients for http requests to other digital identity servers
    /// </summary>
    public class DotYouHttpClientFactory : IDotYouHttpClientFactory
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ITenantCertificateService _tenantCertificateService;
        private readonly TenantContext _tenantContext;

        public DotYouHttpClientFactory(DotYouContextAccessor contextAccessor, ITenantCertificateService tenantCertificateService, TenantContext tenantContext)
        {
            _contextAccessor = contextAccessor;
            _tenantCertificateService = tenantCertificateService;
            _tenantContext = tenantContext;
        }

        public T CreateClientUsingAccessToken<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken)
        {
            Guard.Argument(clientAuthenticationToken, nameof(clientAuthenticationToken)).NotNull();
            Guard.Argument(clientAuthenticationToken.Id, nameof(clientAuthenticationToken.Id)).Require(x => x != Guid.Empty);
            Guard.Argument(clientAuthenticationToken.AccessTokenHalfKey, nameof(clientAuthenticationToken.AccessTokenHalfKey)).Require(x => x.IsSet());
            Guard.Argument(clientAuthenticationToken, nameof(clientAuthenticationToken)).NotNull();

            return this.CreateClientInternal<T>(odinId, clientAuthenticationToken);
        }

        public T CreateClient<T>(OdinId odinId)
        {
            return this.CreateClientInternal<T>(odinId, null);
        }

        ///
        private T CreateClientInternal<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken)
        {
            Guard.Argument(odinId.Id, nameof(odinId)).NotNull();

            var handler = new HttpClientHandler();

            var cert = _tenantCertificateService.GetSslCertificate(_tenantContext.HostOdinId);
            if (null == cert)
            {
                throw new YouverseSystemException($"No certificate configured for {_tenantContext.HostOdinId}");
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
                    Host = odinId
                }.Uri
            };

#if DEBUG
            client.Timeout = TimeSpan.FromHours(1);
#endif
            
            // client.DefaultRequestHeaders.Add(DotYouHeaderNames.AppId, appId);
            if (null != clientAuthenticationToken)
            {
                //TODO: need to encrypt this token somehow? (shared secret?)
                client.DefaultRequestHeaders.Add(DotYouHeaderNames.ClientAuthToken, clientAuthenticationToken.ToString());
            }


// start hack
#if DEBUG
            if (cert.Subject.Contains("*.youfoundation.id"))
            {
                client.DefaultRequestHeaders.Add("dns_hack", _tenantContext.HostOdinId);
            }
#endif
// end hack

            var ogClient = RestService.For<T>(client);
            return ogClient;
        }
    }
}