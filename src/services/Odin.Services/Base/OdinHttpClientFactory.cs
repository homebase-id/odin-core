using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Storage;
using Odin.Services.Authorization.Capi;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;
using Refit;

namespace Odin.Services.Base
{
    /// <summary>
    /// Creates clients for http requests to other digital identity servers
    /// </summary>
    public class OdinHttpClientFactory(
        IDynamicHttpClientFactory httpClientFactory,
        ICorrelationContext correlationContext,
        OdinConfiguration config,
        ICapiCallbackSession capiCallbackSession,
        OdinIdentity odinIdentity)
        : IOdinHttpClientFactory
    {
        //

        public Task<T> CreateClientUsingAccessTokenAsync<T>(
            OdinId remoteOdinId,
            ClientAuthenticationToken clientAuthenticationToken,
            FileSystemType? fileSystemType = null)
        {
            return CreateClientInternalAsync<T>(remoteOdinId, clientAuthenticationToken, fileSystemType);
        }

        //

        public Task<T> CreateClientAsync<T>(
            OdinId remoteOdinId,
            FileSystemType? fileSystemType = null,
            Dictionary<string, string> headers = null)
        {
            return CreateClientInternalAsync<T>(remoteOdinId, null, fileSystemType, headers);
        }

        //

        private async Task<T> CreateClientInternalAsync<T>(
            OdinId remoteOdinId,
            ClientAuthenticationToken clientAuthenticationToken,
            FileSystemType? fileSystemType,
            Dictionary<string, string> headers = null)
        {
            var remoteHost = DnsConfigurationSet.PrefixCertApi + "." + remoteOdinId;
            var httpClient = httpClientFactory.CreateClient($"{nameof(OdinHttpClientFactory)}:{remoteHost}", cfg =>
            {
                cfg.AllowUntrustedServerCertificate = config.CertificateRenewal.UseCertificateAuthorityProductionServers == false;
            });

            var sessionId = await capiCallbackSession.EstablishSessionAsync(remoteOdinId.DomainName, config.Host.CapiSessionLifetime);
            var localDomainAndSessionId = $"{odinIdentity.PrimaryDomain}~{sessionId}";
            httpClient.DefaultRequestHeaders.Add(ICapiCallbackSession.SessionHttpHeaderName, localDomainAndSessionId);
            
            httpClient.BaseAddress = new UriBuilder
            {
                Scheme = "https",
                Host = remoteHost,
                Port = config.Host.DefaultHttpsPort
            }.Uri;

            httpClient.DefaultRequestHeaders.Add(OdinHeaderNames.CorrelationId, correlationContext.Id);

            if (fileSystemType.HasValue)
            {
                httpClient.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, fileSystemType.Value.ToString());
            }

#if DEBUG
            httpClient.Timeout = TimeSpan.FromHours(1);
#endif

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