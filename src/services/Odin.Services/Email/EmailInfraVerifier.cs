using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        var mailgun = configuration.Mailgun;

        // Tenant mail (mailboxes we serve for identities) and Mailgun (how this host sends its
        // own mail to users) are unrelated, so neither gates the other. Only their own state is
        // reported here.
        if (email.TenantMail.Enabled)
        {
            if (email.Stalwart.IsConfigured)
            {
                logger.LogInformation("Tenant mailbox provider: Stalwart at {baseUrl}", email.Stalwart.BaseUrl);
            }
            else
            {
                logger.LogWarning(
                    "Tenant mailbox provider: none (Email:Stalwart:BaseUrl unset); mailbox and app-password " +
                    "actions will succeed without reaching a mail server");
            }
        }

        if (!mailgun.Enabled)
        {
            logger.LogInformation("Mailgun is not enabled; this host sends no mail of its own");
        }

        return mailgun.Enabled || email.TenantMail.Enabled;
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

        if (configuration.Mailgun.Enabled)
        {
            try
            {
                if (!await emailSender.VerifyCredentialsAsync(cancellationToken))
                {
                    errors.Add("Mailgun credential check failed");
                }
            }
            catch (Exception e)
            {
                errors.Add($"Mailgun credential check failed: {e.Message}");
            }
        }

        if (email.TenantMail.Enabled)
        {
            await VerifyTenantMailInfraAsync(email.TenantMail, errors, cancellationToken);
        }

        return errors;
    }

    /// <summary>
    /// Forward-confirmed reverse DNS (FCrDNS) for every MX address: the IP must have a PTR,
    /// and that PTR name must resolve back to the same IP.
    ///
    /// This is what receiving mail servers actually test. An IP with no PTR, or one whose
    /// PTR does not confirm, gets its mail spam-foldered or refused outright - and nothing
    /// else here would notice, because the forward records can all be perfect while the
    /// reverse zone (owned by the hosting provider, not by us) is empty.
    ///
    /// Errors, not warnings: on a host actually serving mail this is the difference between
    /// mail arriving and mail disappearing.
    /// </summary>
    private async Task VerifyReverseDnsAsync(
        string node,
        List<IPAddress> addresses,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        foreach (var address in addresses)
        {
            try
            {
                var reverse = await dnsClient.QueryReverseAsync(address, cancellationToken);
                var ptrNames = reverse.Answers.PtrRecords()
                    .Select(x => x.PtrDomainName.Value.TrimEnd('.'))
                    .ToList();

                if (ptrNames.Count == 0)
                {
                    errors.Add(
                        $"MX node '{node}' address {address} has no PTR record; receiving servers will treat its mail as spam");
                    continue;
                }

                var confirmed = false;
                foreach (var ptrName in ptrNames)
                {
                    var forward = await dnsClient.QueryAsync(ptrName, QueryType.A, cancellationToken: cancellationToken);
                    if (forward.Answers.ARecords().Any(x => x.Address.Equals(address)))
                    {
                        confirmed = true;
                        break;
                    }
                }

                if (!confirmed)
                {
                    errors.Add(
                        $"MX node '{node}' address {address} has PTR '{ptrNames[0]}' but it does not resolve back to {address}; " +
                        "forward-confirmed reverse DNS fails");
                    continue;
                }

                // Forward-confirmation is necessary but not sufficient. A hosting provider's
                // default PTR (e.g. static.<ip>.clients.your-server.de) confirms perfectly while
                // matching nothing we send: receivers - Gmail notably - compare the PTR against
                // the name the server greets with, and a mismatch costs reputation even though
                // every check above passes. Seen live on the bleeding-edge host.
                //
                // A warning rather than an error: mail still flows, and the fix is a support
                // ticket with the hosting provider rather than anything we control.
                if (!ptrNames.Any(x => string.Equals(x, node, StringComparison.OrdinalIgnoreCase)))
                {
                    logger.LogWarning(
                        "MX node '{node}' address {address} has PTR '{ptr}', which does not match the node's hostname; " +
                        "receivers that compare reverse DNS against the EHLO name will treat its mail as less trustworthy",
                        node, address, ptrNames[0]);
                }
            }
            catch (Exception e)
            {
                errors.Add($"MX node '{node}' reverse DNS lookup for {address} failed: {e.Message}");
            }
        }
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
                if (response.Answers.CnameRecords().Any())
                {
                    // The A query follows the chain and returns addresses anyway, so
                    // without this inspection a CNAME'd node would silently pass
                    errors.Add($"MX node '{node}' is a CNAME; RFC 2181 requires MX targets to be address records");
                    continue;
                }

                var addresses = response.Answers.ARecords().Select(x => x.Address).ToList();
                if (addresses.Count == 0)
                {
                    errors.Add($"MX node '{node}' does not resolve to an A record");
                    continue;
                }

                await VerifyReverseDnsAsync(node, addresses, errors, cancellationToken);
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
