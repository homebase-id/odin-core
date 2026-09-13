using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Http;
using Odin.Services.Configuration;
using Odin.Services.Dns.Health;
using Odin.Services.Email;

namespace Odin.Services.Background.BackgroundServices.System;

#nullable enable

/// <summary>
/// One-shot post-startup sanity checks that are too network-heavy to run inline during boot
/// (design: docs/email-dns-plan.md "Startup &amp; ongoing verification"). Each check group
/// retries with backoff and logs ERR only after the final attempt, so a boot-time DNS or
/// network blip does not false-alarm. Failures log ERR for the existing log monitoring;
/// nothing here blocks startup.
/// </summary>
public class StartupVerificationBackgroundService(
    ILogger<StartupVerificationBackgroundService> logger,
    OdinConfiguration config,
    IDynamicHttpClientFactory httpClientFactory,
    EmailInfraVerifier emailInfraVerifier,
    DnsInfraVerifier dnsInfraVerifier)
    : AbstractBackgroundService(logger)
{
    internal static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(90),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            VerifyCdnAsync(stoppingToken),
            VerifyEmailAsync(stoppingToken),
            VerifyDnsInfraAsync(stoppingToken));
        // Run-once: the service exits when the checks complete
    }

    private async Task VerifyCdnAsync(CancellationToken stoppingToken)
    {
        if (!config.Cdn.Enabled)
        {
            return;
        }

        var url = $"{config.Cdn.PayloadBaseUrl}/ping";
        for (var attempt = 0;; attempt++)
        {
            string error;
            try
            {
                var httpClient = httpClientFactory.CreateClient("CdnPingClient");
                var response = await httpClient.GetAsync(url, stoppingToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
                error = response.ReasonPhrase ?? response.StatusCode.ToString();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (attempt < RetryDelays.Length)
            {
                logger.LogWarning("CDN ping at {url} failed ({error}); retrying in {delay}",
                    url, error, RetryDelays[attempt]);
                await Task.Delay(RetryDelays[attempt], stoppingToken);
            }
            else
            {
                config.Cdn.Enabled = false;
                logger.LogError("CDN enabled, but not responding to ping at {url}. Error: {error}", url, error);
                return;
            }
        }
    }

    private async Task VerifyEmailAsync(CancellationToken stoppingToken)
    {
        if (!emailInfraVerifier.LogConfigurationFindings())
        {
            return;
        }

        for (var attempt = 0;; attempt++)
        {
            var errors = await emailInfraVerifier.VerifyNetworkAsync(stoppingToken);
            if (errors.Count == 0)
            {
                logger.LogInformation("Email infrastructure verification passed");
                return;
            }

            if (attempt < RetryDelays.Length)
            {
                logger.LogWarning("Email infrastructure verification found {count} issue(s); retrying in {delay}",
                    errors.Count, RetryDelays[attempt]);
                await Task.Delay(RetryDelays[attempt], stoppingToken);
            }
            else
            {
                foreach (var error in errors)
                {
                    logger.LogError("Email infrastructure verification failed: {error}", error);
                }
                return;
            }
        }
    }

    private async Task VerifyDnsInfraAsync(CancellationToken stoppingToken)
    {
        if (!dnsInfraVerifier.HasChecks)
        {
            return;
        }

        for (var attempt = 0;; attempt++)
        {
            var result = await dnsInfraVerifier.VerifyAsync(stoppingToken);
            if (result.IsClean)
            {
                logger.LogInformation("DNS infrastructure verification passed");
                return;
            }

            if (attempt < RetryDelays.Length)
            {
                logger.LogWarning("DNS infrastructure verification found {count} issue(s); retrying in {delay}",
                    result.Errors.Count + result.Warnings.Count, RetryDelays[attempt]);
                await Task.Delay(RetryDelays[attempt], stoppingToken);
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    logger.LogError("DNS infrastructure verification failed: {error}", error);
                }
                foreach (var warning in result.Warnings)
                {
                    logger.LogWarning("DNS infrastructure: {warning}", warning);
                }
                return;
            }
        }
    }
}
