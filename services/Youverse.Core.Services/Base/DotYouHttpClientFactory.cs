using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
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
        // Per-tenant HttpClient pool
        private readonly ConcurrentDictionary<string, HttpClientWithCertificate> _httpClients = new ();

        private readonly ICertificateServiceFactory _certificateServiceFactory;
        private readonly TenantContext _tenantContext;

        public DotYouHttpClientFactory(
            ICertificateServiceFactory certificateServiceFactory, 
            TenantContext tenantContext)
        {
            _certificateServiceFactory = certificateServiceFactory;
            _tenantContext = tenantContext;
        }
        
        //

        public T CreateClient<T>(OdinId odinId)
        {
            Guard.Argument(odinId.DomainName, nameof(odinId)).NotNull();

            var remoteHost = DnsConfigurationSet.PrefixCertApi + "." + odinId;
            var httpClient = GetHttpClient(remoteHost);
            
            //
            // IMPORTANT IMPORTANT IMPORTANT IMPORTANT IMPORTANT IMPORTANT 
            //
            // httpClient is now a shared instance for this tenant.
            // While using a HttpClient is thread safe, changing *anything* on it is not.
            // This means we MUST NOT set e.g. default headers, timeouts, etc
            //

            return RestService.For<T>(httpClient);
        }
        
        //
        
        
        public (T refitClient, Dictionary<string, string> httpHeaders) CreateClientAndHeaders<T>(
            OdinId odinId, 
            ClientAuthenticationToken clientAuthenticationToken = null, 
            FileSystemType? fileSystemType = null)
        {
            var client = CreateClient<T>(odinId);
            var httpHeaders = CreateHeaders(clientAuthenticationToken, fileSystemType);
            return (client, httpHeaders);
        }
        
        //
        
        public Dictionary<string, string> CreateHeaders(
            ClientAuthenticationToken clientAuthenticationToken = null,
            FileSystemType? fileSystemType = null)
        {
            var result = new Dictionary<string, string>();
            
            if (clientAuthenticationToken != null)
            {
                result.Add(DotYouHeaderNames.ClientAuthToken, clientAuthenticationToken.ToString());
            }
            
            if (fileSystemType != null)
            {
                result.Add(DotYouHeaderNames.FileSystemTypeHeader, fileSystemType.ToString());
            }

            return result;
        }

        //

        private HttpClient GetHttpClient(string remoteHost)
        {
            var clientWithCert = _httpClients.GetOrAdd(remoteHost, _ => CreateHttpClient(remoteHost));

            var x509 = clientWithCert.Certificate;
            var now = DateTime.Now;
            if (now >= x509.NotBefore && now <= x509.NotAfter)
            {
                return clientWithCert.HttpClient;
            }
           
            // Certificate expired, create a new HttpClient (implies updated certificate)
            clientWithCert = _httpClients.AddOrUpdate(
                remoteHost, 
                (_)   => CreateHttpClient(remoteHost), 
                (_,_) => CreateHttpClient(remoteHost));
            
            return clientWithCert.HttpClient;
        }
        
        //
        
        private class HttpClientWithCertificate
        {
            public HttpClient HttpClient { get; init; }
            public X509Certificate2 Certificate { get; init; }
        }
    
        //

        private HttpClientWithCertificate CreateHttpClient(string remoteHost)
        {
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

            var client = new HttpClient(handler)
            {
                BaseAddress = new UriBuilder()
                {
                    Scheme = "https",
                    Host = remoteHost
                }.Uri
            };
            
#if DEBUG
            client.Timeout = TimeSpan.FromHours(1);
#endif

            return new HttpClientWithCertificate
            {
                HttpClient = client,
                Certificate = cert
            };
        }
        
        //

    }
    
    //
   
    
}

