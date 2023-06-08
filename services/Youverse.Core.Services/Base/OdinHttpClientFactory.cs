using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Dawn;
using HttpClientFactoryLite;
using Refit;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Logging.CorrelationId;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Registry.Registration;
using Youverse.Core.Storage;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Creates clients for http requests to other digital identity servers
    /// </summary>
    public class OdinHttpClientFactory : IOdinHttpClientFactory
    {
        private readonly IHttpClientFactory _httpClientFactory;
        
        private readonly ICertificateServiceFactory _certificateServiceFactory;
        private readonly TenantContext _tenantContext;
        private readonly ICorrelationContext _correlationContext;

        public static string HttpFactoryKey(string domain) => $"{nameof(OdinHttpClientFactory)}.{domain}"; 
        
        public OdinHttpClientFactory(
            IHttpClientFactory httpClientFactory,
            ICertificateServiceFactory certificateServiceFactory, 
            TenantContext tenantContext, 
            ICorrelationContext correlationContext)
        {
            _httpClientFactory = httpClientFactory;
            _certificateServiceFactory = certificateServiceFactory;
            _tenantContext = tenantContext;
            _correlationContext = correlationContext;
        }
        
        //
        
        public T CreateClientUsingAccessToken<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType = null)
        {
            Guard.Argument(clientAuthenticationToken, nameof(clientAuthenticationToken)).NotNull();
            // Guard.Argument(clientAuthenticationToken.Id, nameof(clientAuthenticationToken.Id)).Require(x => x != Guid.Empty);
            // Guard.Argument(clientAuthenticationToken.AccessTokenHalfKey, nameof(clientAuthenticationToken.AccessTokenHalfKey)).Require(x => x.IsSet());
            Guard.Argument(clientAuthenticationToken, nameof(clientAuthenticationToken)).NotNull();

            return this.CreateClientInternal<T>(odinId, clientAuthenticationToken, fileSystemType);
        }
        
        //
        
        public T CreateClient<T>(OdinId odinId, FileSystemType? fileSystemType = null)
        {
            return this.CreateClientInternal<T>(odinId, null, fileSystemType);
        }
        
        //
        
        private T CreateClientInternal<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType)
        {
            Guard.Argument(odinId.DomainName, nameof(odinId)).NotNull();

            var httpClientKey = HttpFactoryKey(_tenantContext.HostOdinId.DomainName);
            var httpClient = _httpClientFactory.CreateClient(httpClientKey);
            var remoteHost = DnsConfigurationSet.PrefixCertApi + "." + odinId;
            httpClient.BaseAddress = new UriBuilder() { Scheme = "https", Host = remoteHost }.Uri;
            httpClient.DefaultRequestHeaders.Add(ICorrelationContext.DefaultHeaderName, _correlationContext.Id);
            
            if (fileSystemType.HasValue)
            {
                httpClient.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, fileSystemType.Value.ToString());
            }

#if DEBUG
            httpClient.Timeout = TimeSpan.FromHours(1);
#endif

            // client.DefaultRequestHeaders.Add(DotYouHeaderNames.AppId, appId);
            if (null != clientAuthenticationToken)
            {
                //TODO: need to encrypt this token somehow? (shared secret?)
                httpClient.DefaultRequestHeaders.Add(OdinHeaderNames.ClientAuthToken, clientAuthenticationToken.ToString());
            }
            
            var ogClient = RestService.For<T>(httpClient);
            return ogClient;
        }

        //

    }
    
    //
   
    
}

