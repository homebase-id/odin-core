using System;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Certificate;
using Odin.Core.Services.Registry.Registration;
using Odin.Core.Storage;
using Refit;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Odin.Core.Services.Base
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

