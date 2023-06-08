using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Certes.Pkcs;
using HttpClientFactoryLite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;

namespace Odin.Core.Services.Certificate;

#nullable enable

// Certes:
// https://github.com/fszlin/certes
// https://github.com/fszlin/certes/blob/main/docs/APIv2.md

// Parallel test
// seq 1 10 | xargs -Iname -P10  curl -v "https://sebbarg.net" -o /dev/null

public sealed class CertesAcme : ICertesAcme
{
    private readonly ILogger<CertesAcme> _logger;
    private readonly IAcmeHttp01TokenCache _tokenCache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Uri _directoryUri;
    
    public bool IsProduction { get; }

    public CertesAcme(
        ILogger<CertesAcme> logger, 
        IAcmeHttp01TokenCache tokenCache, 
        IHttpClientFactory httpClientFactory, 
        bool isProduction)
    {
        _logger = logger;
        _tokenCache = tokenCache;
        _httpClientFactory = httpClientFactory;
        IsProduction = isProduction;

        _directoryUri = IsProduction
            ? WellKnownServers.LetsEncryptV2
            : WellKnownServers.LetsEncryptStagingV2;
    }
    
    //

    public async Task<AcmeAccount> CreateAccount(string contactEmail)
    {
        _logger.LogDebug("Creating account for {contactEmail}", contactEmail);
        
        var acme = new AcmeContext(_directoryUri);
        await acme.NewAccount(contactEmail, true);
        return new AcmeAccount { AccounKeyPem = acme.AccountKey.ToPem() };
    }
    
    //

    public async Task<KeysAndCertificates> CreateCertificate(AcmeAccount acmeAccount, string[] domains)
    {
        var sw = Stopwatch.StartNew();
        
        // Sanity
        if (domains.Length == 0)
        {
            throw new YouverseSystemException("Missing damains");
        }
        
        _logger.LogDebug("Creating certificate for {domains}", string.Join(',', domains));
        
        //
        // Access account
        //
        var accountKey = KeyFactory.FromPem(acmeAccount.AccounKeyPem);
        var acme = new AcmeContext(_directoryUri, accountKey);
        
        //
        // Create private key
        //
        var privateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
        var privateKeyPem = privateKey.ToPem();

        // 
        // Create certificate signing request structure
        //  
        var csr = new CertificationRequestBuilder(privateKey);
        csr.AddName($"CN={domains[0]}");
        for (var idx = 1; idx < domains.Length; idx++)
        {
            csr.SubjectAlternativeNames.Add(domains[idx]);
        }
        
        //
        // Create order
        //
        var order = await acme.NewOrder(domains);
        
        //
        // Authorize and challange
        //
        var authzs = await order.Authorizations();
        
        foreach (var authz in authzs)
        {
            var challenge = await authz.Http();

            _logger.LogDebug("Adding challenge token {token}", challenge.Token);
            _tokenCache.Set(challenge.Token, challenge.KeyAuthz);

            await challenge.Validate();
        }
        
        //
        // Wait for all authorizations to be valid
        //
        authzs = await order.Authorizations();
        foreach (var authz in authzs)
        {
            var resource = await authz.Resource();
            var maxAttempts = 120;
            while (--maxAttempts > 0 && resource.Status != AuthorizationStatus.Valid)
            {
                await Task.Delay(1000);
                resource = await authz.Resource();
            }

            if (resource.Status != AuthorizationStatus.Valid)
            {
                throw new YouverseSystemException(
                    $"Failed or timed out validating one or more challenges. Status: {resource.Status}");
            }
        }

        //
        // Finalize order and wait for order status to be valid
        //
        await order.Finalize(csr.Generate());
        {
            var resource = await order.Resource();
            var maxAttempts = 120;
            while (--maxAttempts > 0 && resource.Status != OrderStatus.Valid)
            {
                await Task.Delay(1000);
                resource = await order.Resource();
            }
        
            if (resource.Status != OrderStatus.Valid)
            {
                throw new YouverseSystemException(
                    $"Failed or timed out finalizing order. Status: {resource.Status}");
            }
        }

        //
        // Download certificate
        //
        var cert = await order.Download();

        //
        // If we are using LetsEncrypt staging servers, we need to add their root certificates
        // or BouncyCastle will blow up when generating full-chain certificates.
        //
        // Staging env: https://letsencrypt.org/docs/staging-environment/
        //
        // See also:
        // https://community.letsencrypt.org/t/can-not-find-issuer-c-us-o-staging-internet-security-research-group-cn-staging-doctored-durian-root-ca-x3-for-certificate-c-us-o-staging-internet-security-research-group-cn-staging-pretend-pear-x1/147613
        //
        string certificatesPem;
        if (IsProduction)
        {
            certificatesPem = cert.ToPem();
        }
        else
        {
            var pfxBuilder = cert.ToPfx(privateKey);
            pfxBuilder.FullChain = true;

            var stagingRoots = await DownloadStagingRootCerts();
            foreach (var stagingRoot in stagingRoots)
            {
                var bytes = Encoding.ASCII.GetBytes(stagingRoot);
                pfxBuilder.AddIssuer(bytes);
            }

            var pfx = pfxBuilder.Build("letsencrypt-cert", "doesnt-matter");
            var xcert = new X509Certificate2(pfx, "doesnt-matter");
            var chain = new X509Chain();
            chain.Build(xcert);
            var sb = new StringBuilder();
            foreach (var element in chain.ChainElements)
            {
                sb.AppendLine("-----BEGIN CERTIFICATE-----");
                sb.AppendLine(Convert.ToBase64String(element.Certificate.Export(X509ContentType.Cert),
                    Base64FormattingOptions.InsertLineBreaks));
                sb.AppendLine("-----END CERTIFICATE-----");
            }

            certificatesPem = sb.ToString();
        }

        _logger.LogDebug("Certificate for {domains} created in {elapsed}s", 
            string.Join(',', domains), sw.ElapsedMilliseconds / 1000.0);
        
        return new KeysAndCertificates
        {
            PrivateKeyPem = privateKeyPem,
            CertificatesPem = certificatesPem
        };
    }
    
    //
    
    private async Task<List<string>> DownloadStagingRootCerts()
    {
        var result = new List<string>();
        var uris = new[]
        {
            "https://letsencrypt.org/certs/staging/letsencrypt-stg-root-x1.pem", 
            "https://letsencrypt.org/certs/staging/letsencrypt-stg-root-x2.pem" 
        };

        var httpClient = _httpClientFactory.CreateClient<CertesAcme>();
        foreach (var uri in uris)
        {
            _logger.LogInformation("Downloading staging certitiate: {uri}", uri);
            var response = await httpClient.GetAsync(uri);
            var cert = await response.Content.ReadAsStringAsync();
            result.Add(cert);
        }

        return result;
    }
    
    //
}

//

public sealed class AcmeHttp01TokenCache : IAcmeHttp01TokenCache
{
    private readonly MemoryCache _cache;

    public AcmeHttp01TokenCache()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    //

    public bool TryGet(string token, out string keyAuth)
    {
        var found = _cache.TryGetValue(token, out string? key);

        if (found && key != null)
        {
            keyAuth = key;
            return true;
        }

        keyAuth = "";
        return false;
    }

    //

    public void Set(string token, string keyAuth)
    {
        _cache.Set(token, keyAuth, TimeSpan.FromMinutes(10));
    }

    //

}
