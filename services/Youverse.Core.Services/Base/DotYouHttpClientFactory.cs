using System;
using System.Net.Http;
using System.Security.Authentication;
using Dawn;
using Refit;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Registry.Registration;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Creates clients for http requests to other digital identity servers
    /// </summary>
    public class DotYouHttpClientFactory : IDotYouHttpClientFactory
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ICertificateServiceFactory _certificateServiceFactory;
        private readonly TenantContext _tenantContext;

        public DotYouHttpClientFactory(
            DotYouContextAccessor contextAccessor, 
            ICertificateServiceFactory certificateServiceFactory, 
            TenantContext tenantContext)
        {
            _contextAccessor = contextAccessor;
            _certificateServiceFactory = certificateServiceFactory;
            _tenantContext = tenantContext;
        }

        public T CreateClientUsingAccessToken<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType = null)
        {
            Guard.Argument(clientAuthenticationToken, nameof(clientAuthenticationToken)).NotNull();
            // Guard.Argument(clientAuthenticationToken.Id, nameof(clientAuthenticationToken.Id)).Require(x => x != Guid.Empty);
            // Guard.Argument(clientAuthenticationToken.AccessTokenHalfKey, nameof(clientAuthenticationToken.AccessTokenHalfKey)).Require(x => x.IsSet());
            Guard.Argument(clientAuthenticationToken, nameof(clientAuthenticationToken)).NotNull();

            return this.CreateClientInternal<T>(odinId, clientAuthenticationToken, fileSystemType);
        }

        public T CreateClient<T>(OdinId odinId, FileSystemType? fileSystemType = null)
        {
            return this.CreateClientInternal<T>(odinId, null, fileSystemType);
        }

        ///
        private T CreateClientInternal<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType)
        {
            Guard.Argument(odinId.DomainName, nameof(odinId)).NotNull();

            var handler = new HttpClientHandler();

            var certificateService = _certificateServiceFactory.Create(_tenantContext.SslRoot);
            var cert = certificateService.GetSslCertificate(_tenantContext.HostOdinId);
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
                    Host = DnsConfigurationSet.PrefixCertApi + "." + odinId
                }.Uri
            };

            if (fileSystemType.HasValue)
            {
                client.DefaultRequestHeaders.Add(DotYouHeaderNames.FileSystemTypeHeader, fileSystemType.Value.ToString());
            }

#if DEBUG
            client.Timeout = TimeSpan.FromHours(1);
#endif

            // client.DefaultRequestHeaders.Add(DotYouHeaderNames.AppId, appId);
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