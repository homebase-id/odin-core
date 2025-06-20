using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Storage;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;
using Refit;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Odin.Services.Base
{
    /// <summary>
    /// Creates clients for http requests to other digital identity servers
    /// </summary>
    public class OdinHttpClientFactory : IOdinHttpClientFactory
    {
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly ICertificateService _certificateService;
        private readonly TenantContext _tenantContext;
        private readonly ICorrelationContext _correlationContext;
        private readonly OdinConfiguration _config;

        public static string HttpFactoryKey(string domain) => $"{nameof(OdinHttpClientFactory)}.{domain}";

        public OdinHttpClientFactory(
            IHttpClientFactory httpClientFactory,
            ICertificateService certificateService,
            TenantContext tenantContext,
            ICorrelationContext correlationContext,
            OdinConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _certificateService = certificateService;
            _tenantContext = tenantContext;
            _correlationContext = correlationContext;
            _config = config;
        }

        //

        public T CreateClientUsingAccessToken<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType = null)
        {
            return this.CreateClientInternal<T>(odinId, clientAuthenticationToken, fileSystemType);
        }

        //

        public T CreateClient<T>(OdinId odinId, FileSystemType? fileSystemType = null, Dictionary<string, string> headers = null)
        {
            return this.CreateClientInternal<T>(odinId, null, fileSystemType, headers);
        }

        //

        private T CreateClientInternal<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType,
            Dictionary<string, string> headers = null)
        {
            var httpClientKey = HttpFactoryKey(_tenantContext.HostOdinId.DomainName);
            var httpClient = _httpClientFactory.CreateClient(httpClientKey);
            var remoteHost = DnsConfigurationSet.PrefixCertApi + "." + odinId;
            httpClient.BaseAddress = new UriBuilder
            {
                Scheme = "https",
                Host = remoteHost,
                Port = _config.Host.DefaultHttpsPort
            }.Uri;
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

            if (null != headers)
            {
                foreach (var header in headers)
                {
                    if (!httpClient.DefaultRequestHeaders.TryGetValues(header.Key, out _))
                    {
                        httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                    }
                }
            }

            var ogClient = RestService.For<T>(httpClient);
            return ogClient;
        }

        //
    }

    //
}