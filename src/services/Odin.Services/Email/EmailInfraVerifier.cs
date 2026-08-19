using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Odin.Core.Http;
using Odin.Services.Configuration;

#nullable enable

namespace Odin.Services.Email;

/// <summary>
/// Email infrastructure checks run by StartupVerificationBackgroundService.
/// Uses generic public DNS lookups only - never the PowerDNS API (see docs/byod-dnssec-plan.md
/// for the boundary; docs/email-dns-plan.md for the check design). Each failure class is
/// caught by the cheapest mechanism that can see it - there is never a tenant traversal here.
/// </summary>
public class EmailInfraVerifier(
    ILogger<EmailInfraVerifier> logger,
    OdinConfiguration configuration,
    ILookupClient dnsClient,
    IDynamicHttpClientFactory httpClientFactory,
    IEmailSender emailSender)
{
    /// <summary>
    /// Logs the config-derived findings once (these never change between retries).
    /// Returns true when network-dependent verification remains to be run.
    /// </summary>
    public bool LogConfigurationFindings()
    {
        var email = configuration.Email;

        if (email.LegacyMailgunConfig)
        {
            logger.LogWarning(
                "Deprecated top-level Mailgun config section in use; move to the consolidated Email section - the fallback is removed next release");
        }

        if (email.TenantMail.Enabled && email.Provider == EmailProvider.None)
        {
            logger.LogError(
                "Email:TenantMail:Enabled is true but Email:Provider is 'None'; tenant mail stays disabled until a provider is configured");
        }

        switch (email.Provider)
        {
            case EmailProvider.None:
                logger.LogInformation("Email provider is 'None'; system mail is disabled");
                break;
            case EmailProvider.Smtp:
                logger.LogWarning(
                    "Email provider 'Smtp' (self-sending) is not implemented yet; mail is disabled (see docs/email-keys-plan.md)");
                break;
        }

        var checkCredentials = email.Provider is EmailProvider.SendGrid or EmailProvider.Mailgun;
        var checkTenantMailInfra = email.TenantMail.Enabled && email.Provider != EmailProvider.None;
        return checkCredentials || checkTenantMailInfra;
    }

    /// <summary>
    /// Runs the network-dependent checks once and returns the failures.
    /// The caller retries with backoff and logs ERR only after the final attempt,
    /// so a boot-time DNS blip does not false-alarm.
    /// </summary>
    public async Task<List<string>> VerifyNetworkAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var email = configuration.Email;

        if (email.Provider is EmailProvider.SendGrid or EmailProvider.Mailgun)
        {
            try
            {
                if (!await emailSender.VerifyCredentialsAsync(cancellationToken))
                {
                    errors.Add($"'{email.Provider}' provider credential check failed");
                }
            }
            catch (Exception e)
            {
                errors.Add($"'{email.Provider}' provider credential check failed: {e.Message}");
            }
        }

        if (email.TenantMail.Enabled && email.Provider != EmailProvider.None)
        {
            await VerifyTenantMailInfraAsync(email.TenantMail, errors, cancellationToken);
        }

        return errors;
    }

    private async Task VerifyTenantMailInfraAsync(
        OdinConfiguration.TenantMailSection tenantMail,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        // Every MX node must resolve - HA only works if all listed nodes actually serve.
        // MX targets must be A records, not CNAMEs (RFC 2181).
        foreach (var node in tenantMail.MxNodes)
        {
            try
            {
                var response = await dnsClient.QueryAsync(node, QueryType.A, cancellationToken: cancellationToken);
                if (!response.Answers.ARecords().Any())
                {
                    errors.Add($"MX node '{node}' does not resolve to an A record");
                }
            }
            catch (Exception e)
            {
                errors.Add($"MX node '{node}' A record lookup failed: {e.Message}");
            }
        }

        // The single place outbound is authorized; every tenant SPF record includes it
        try
        {
            var response = await dnsClient.QueryAsync(tenantMail.SpfIncludeTarget, QueryType.TXT,
                cancellationToken: cancellationToken);
            // A long TXT value arrives as multiple <=255-byte character strings; concatenate before matching
            var texts = response.Answers.TxtRecords().Select(x => string.Concat(x.Text)).ToList();
            if (!texts.Any(x => x.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"SPF include target '{tenantMail.SpfIncludeTarget}' has no v=spf1 TXT record");
            }
        }
        catch (Exception e)
        {
            errors.Add($"SPF include target '{tenantMail.SpfIncludeTarget}' TXT lookup failed: {e.Message}");
        }

        // MTA-STS policy endpoint, probed on the canary identity's own surface
        if (string.IsNullOrEmpty(tenantMail.CanaryDomain))
        {
            logger.LogDebug("No Email:TenantMail:CanaryDomain configured; skipping MTA-STS policy endpoint check");
        }
        else
        {
            var host = $"mta-sts.{tenantMail.CanaryDomain}";
            var url = $"https://{host}/.well-known/mta-sts.txt";
            try
            {
                var httpClient = httpClientFactory.CreateClient(host);
                var response = await httpClient.GetAsync(url, cancellationToken);
                var body = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : "";
                if (!response.IsSuccessStatusCode || !body.Contains("version: STSv1"))
                {
                    errors.Add($"MTA-STS policy endpoint '{url}' is not serving a valid policy");
                }
            }
            catch (Exception e)
            {
                errors.Add($"MTA-STS policy endpoint '{url}' is unreachable: {e.Message}");
            }
        }

        // TLSA / port-25 certificate agreement per MX node is deliberately absent until the
        // mail server exists (docs/email-keys-plan.md) - the records are ops-written infra-zone
        // items and nothing listens on port 25 yet, so the check would ERR permanently.
    }
}
