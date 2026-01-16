using System;
using System.Collections.Generic;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Storage;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;
using Refit;

namespace Odin.Services.Base
{
    /// <summary>
    /// Creates clients for http requests to other digital identity servers
    /// </summary>
    public class OdinHttpClientFactory : IOdinHttpClientFactory
    {
        private readonly IDynamicHttpClientFactory _httpClientFactory;
        private readonly ICorrelationContext _correlationContext;
        private readonly OdinConfiguration _config;
        private readonly ICertificateStore _certificateStore;
        private readonly OdinIdentity _odinIdentity;

        public OdinHttpClientFactory(
            IDynamicHttpClientFactory httpClientFactory,
            ICorrelationContext correlationContext,
            OdinConfiguration config,
            ICertificateStore certificateStore,
            OdinIdentity odinIdentity)
        {
            _httpClientFactory = httpClientFactory;
            _correlationContext = correlationContext;
            _config = config;
            _certificateStore = certificateStore;
            _odinIdentity = odinIdentity;
        }

        //

        public T CreateClientUsingAccessToken<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType = null)
        {
            return CreateClientInternal<T>(odinId, clientAuthenticationToken, fileSystemType);
        }

        //

        public T CreateClient<T>(OdinId odinId, FileSystemType? fileSystemType = null, Dictionary<string, string> headers = null)
        {
            return CreateClientInternal<T>(odinId, null, fileSystemType, headers);
        }

        //

        private T CreateClientInternal<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType,
            Dictionary<string, string> headers = null)
        {
            var remoteHost = DnsConfigurationSet.PrefixCertApi + "." + odinId;
            var httpClient = _httpClientFactory.CreateClient($"{nameof(OdinHttpClientFactory)}:{remoteHost}", cfg =>
            {
                cfg.AllowUntrustedServerCertificate =
                    _config.CertificateRenewal.UseCertificateAuthorityProductionServers == false;

                cfg.ClientCertificate = _certificateStore.GetCertificateAsync(_odinIdentity.PrimaryDomain).Result;

                // Sanity
                if (cfg.ClientCertificate == null)
                {
                    throw new OdinSystemException($"No client certificate found for domain {_odinIdentity.PrimaryDomain}");
                }
            });

            httpClient.BaseAddress = new UriBuilder
            {
                Scheme = "https",
                Host = remoteHost,
                Port = _config.Host.DefaultHttpsPort
            }.Uri;

            httpClient.DefaultRequestHeaders.Add(OdinHeaderNames.CorrelationId, _correlationContext.Id);

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