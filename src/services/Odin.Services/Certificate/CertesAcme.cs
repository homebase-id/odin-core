using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Certes.Pkcs;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Storage.Cache;

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
    private readonly ISystemLevel2Cache<CertesAcme> _tokenCache;
    private readonly IDynamicHttpClientFactory _httpClientFactory;
    private readonly Uri _directoryUri;
    
    public bool IsProduction { get; }

    public CertesAcme(
        ILogger<CertesAcme> logger, 
        ISystemLevel2Cache<CertesAcme> tokenCache,
        IDynamicHttpClientFactory httpClientFactory,
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

    public async Task<AcmeAccount> CreateAccountAsync(string contactEmail, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating letsencrypt account for {contactEmail}", contactEmail);
        var sw = Stopwatch.StartNew();

        var acme = new AcmeContext(_directoryUri);
        await acme.NewAccount(contactEmail, true);

        _logger.LogDebug("Created letsencrypt account for {contactEmail} in {Elapsed}s",
            contactEmail, sw.ElapsedMilliseconds / 1000.0);

        return new AcmeAccount { AccounKeyPem = acme.AccountKey.ToPem() };
    }
    
    //

    public async Task<KeysAndCertificates> CreateCertificateAsync(AcmeAccount acmeAccount, string[] domains, CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalCreateCertificateAsync(acmeAccount, domains, cancellationToken);
        }
        catch (AcmeRequestException e)
        {
            // Translate the CA's verdict into something the caller can act on without having to
            // parse strings.
            var type = e.Error?.Type ?? "";
            var detail = e.Error?.Detail ?? e.Message;

            if (type.Equals(AcmeRateLimitedErrorType, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Rate limited by the CA for {domains}: {detail}",
                    string.Join(',', domains), detail);
                throw new AcmeRateLimitedException($"{type}: {detail}", DefaultRateLimitRetryAfter);
            }

            if (RetryableErrorTypes.Contains(type))
            {
                // Genuinely transient - the protocol expects us to try again. Surfaced as its
                // own type so the caller can retry it soon rather than treating it as evidence
                // that this domain is failing.
                _logger.LogWarning("Transient ACME error for {domains}: {type}: {detail}",
                    string.Join(',', domains), type, detail);
                throw new AcmeTransientException($"{type}: {detail}");
            }

            // Everything else is the CA rejecting this order on its merits. Ordering again right
            // away produces the same rejection, and at Let's Encrypt it also spends the
            // per-hostname failed-authorization allowance.
            throw new AcmeOrderException($"{type}: {detail}");
        }
    }

    //

    //
    // RFC 8555 §6.7.1: a problem document may carry sub-problems, each naming the identifier it
    // is about. That is how we learn WHICH name of a multi-name order the CA refused.
    //

    // urn:ietf:params:acme:error:rateLimited is per-hostname and per-hour at Let's Encrypt
    // (5 failed authorizations / hostname / hour), so an hour is the correct wait.
    // https://letsencrypt.org/docs/rate-limits/
    //
    // A verdict, not a stage. Polling on past one of these just burns a minute per name - and
    // now that every authorization is checked before throwing, a four-name order where all four
    // fail would otherwise sit in Task.Delay for four minutes while holding the order lock.
    //
    private static bool IsTerminal(AuthorizationStatus? status) =>
        status is AuthorizationStatus.Invalid
            or AuthorizationStatus.Revoked
            or AuthorizationStatus.Deactivated
            or AuthorizationStatus.Expired;

    //

    private const string AcmeRateLimitedErrorType = "urn:ietf:params:acme:error:rateLimited";
    private static readonly TimeSpan DefaultRateLimitRetryAfter = TimeSpan.FromHours(1);

    // RFC 8555 §6.5/§6.6: a bad nonce is expected occasionally and must be retried, and a
    // server-side error says nothing about our order.
    private static readonly HashSet<string> RetryableErrorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "urn:ietf:params:acme:error:badNonce",
        "urn:ietf:params:acme:error:serverInternal",
    };

    //

    private async Task<KeysAndCertificates> InternalCreateCertificateAsync(AcmeAccount acmeAccount, string[] domains, CancellationToken cancellationToken)
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
            await _tokenCache.SetAsync(
                challenge.Token,
                challenge.KeyAuthz,
                TimeSpan.FromMinutes(60),
                cancellationToken: cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await challenge.Validate();
        }

        //
        // Wait for all authorizations to be valid
        //
        cancellationToken.ThrowIfCancellationRequested();
        authzs = await order.Authorizations();

        //
        // Every authorization is checked before throwing, so the error names every refused name
        // rather than just the first. Cheap, because the poll stops the moment a status is
        // terminal, and it is the line an operator will be reading.
        //
        var refused = false;
        var failureDetail = new List<string>();
        var timedOut = false;
        foreach (var authz in authzs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resource = await authz.Resource();
            var maxAttempts = 60;
            while (--maxAttempts > 0 && resource.Status != AuthorizationStatus.Valid && !IsTerminal(resource.Status))
            {
                await Task.Delay(1000, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                resource = await authz.Resource();
            }

            if (resource.Status == AuthorizationStatus.Valid)
            {
                continue;
            }

            var name = resource.Identifier?.Value ?? "";
            failureDetail.Add($"'{name}' ({resource.Status})");

            if (IsTerminal(resource.Status))
            {
                refused = true;
            }
            else
            {
                // Still pending or processing when we gave up waiting. That is our timeout, not
                // the CA's verdict, and must not be reported as a refusal.
                timedOut = true;
            }
        }

        if (refused)
        {
            throw new AcmeOrderException(
                $"The CA refused the challenge for {string.Join(", ", failureDetail)}.");
        }

        if (timedOut)
        {
            throw new AcmeTransientException(
                $"Timed out waiting for the CA to validate {string.Join(", ", failureDetail)}.");
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
                throw new AcmeOrderException(
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

        var httpClient = _httpClientFactory.CreateClient("letsencrypt.org");
        foreach (var uri in uris)
        {
            _logger.LogInformation("Downloading staging certificate: {uri}", uri);
            var response = await httpClient.GetAsync(uri, cancellationToken);
            var cert = await response.Content.ReadAsStringAsync(cancellationToken);
            result.Add(cert);
        }

        return result;
    }
    
    //
}

//

