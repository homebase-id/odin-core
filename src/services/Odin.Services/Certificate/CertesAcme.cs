using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Certes.Pkcs;
using HttpClientFactoryLite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;

namespace Odin.Services.Certificate;

#nullable enable

// SEB:NOTE Certes is no longer maintained and does not support CancellationToken.
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

    public async Task<AcmeAccount> CreateAccount(string contactEmail, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating account for {contactEmail}", contactEmail);
        var sw = Stopwatch.StartNew();

        var acme = new AcmeContext(_directoryUri);
        await acme.NewAccount(contactEmail, true);

        _logger.LogDebug("Created account for {contactEmail} in {Elapsed}s",
            contactEmail, sw.ElapsedMilliseconds / 1000.0);

        return new AcmeAccount { AccounKeyPem = acme.AccountKey.ToPem() };
    }
    
    //

    public async Task<KeysAndCertificates> CreateCertificate(AcmeAccount acmeAccount, string[] domains, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        
        // Sanity
        if (domains.Length == 0)
        {
            throw new OdinSystemException("Missing domains");
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
        cancellationToken.ThrowIfCancellationRequested();
        var order = await acme.NewOrder(domains);
        
        //
        // Authorize and challenge
        //
        cancellationToken.ThrowIfCancellationRequested();
        var authzs = await order.Authorizations();
        
        foreach (var authz in authzs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var challenge = await authz.Http();

            _logger.LogDebug("Adding challenge token {token}", challenge.Token);
            _tokenCache.Set(challenge.Token, challenge.KeyAuthz);

            cancellationToken.ThrowIfCancellationRequested();
            await challenge.Validate();
        }

        //
        // Wait for all authorizations to be valid
        //
        cancellationToken.ThrowIfCancellationRequested();
        authzs = await order.Authorizations();
        foreach (var authz in authzs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resource = await authz.Resource();
            var maxAttempts = 60;
            while (--maxAttempts > 0 && resource.Status != AuthorizationStatus.Valid)
            {
                await Task.Delay(1000, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                resource = await authz.Resource();
            }

            if (resource.Status != AuthorizationStatus.Valid)
            {
                throw new OdinSystemException(
                    $"Failed or timed out validating one or more challenges. Status: {resource.Status}");
            }
        }

        //
        // Finalize order and wait for order status to be valid
        //
        cancellationToken.ThrowIfCancellationRequested();
        await order.Finalize(csr.Generate());
        {
            var resource = await order.Resource();
            var maxAttempts = 60;
            while (--maxAttempts > 0 && resource.Status != OrderStatus.Valid)
            {
                await Task.Delay(1000, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                resource = await order.Resource();
            }
        
            if (resource.Status != OrderStatus.Valid)
            {
                throw new OdinSystemException(
                    $"Failed or timed out finalizing order. Status: {resource.Status}");
            }
        }

        //
        // Download certificate
        //
        cancellationToken.ThrowIfCancellationRequested();
        var cert = await order.Download();

        //
        // If we are using LetsEncrypt staging servers, we need to add their root certificates.
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
            var sb = new StringBuilder();

            sb.AppendLine(cert.Certificate.ToPem());
            foreach (var issuer in cert.Issuers)
            {
                sb.AppendLine(issuer.ToPem());
            }

            var stagingRoots = await DownloadStagingRootCerts(cancellationToken);
            foreach (var stagingRoot in stagingRoots)
            {
                sb.AppendLine(stagingRoot);
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
    
    private async Task<List<string>> DownloadStagingRootCerts(CancellationToken cancellationToken = default)
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
            var response = await httpClient.GetAsync(uri, cancellationToken);
            var cert = await response.Content.ReadAsStringAsync(cancellationToken);
            result.Add(cert);
        }

        return result;
    }
    
    //
}

//

public sealed class AcmeHttp01TokenCache : IAcmeHttp01TokenCache
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

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
        _cache.Set(token, keyAuth, TimeSpan.FromMinutes(60));
    }

    //

}
