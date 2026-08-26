using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Serialization;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.Email.Dkim;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Email.Relay;

/// <summary>
/// SMTP2GO as the outbound relay.
///
/// Chosen because its SMTP credentials are ACCOUNT-level: one smarthost credential may send
/// From: any verified sender domain. Mailgun issues credentials per domain, which is why a
/// single Stalwart relay route could only ever serve one tenant.
///
/// Onboarding publishes two CNAMEs per tenant and touches nothing else - no MX, no apex SPF,
/// and not the tenant's own s1/s2 DKIM. SPF is evaluated against the return-path subdomain,
/// which is theirs, so the apex SPF is not involved in this traffic at all.
/// </summary>
public class Smtp2GoRelayProvider(
    ILogger<Smtp2GoRelayProvider> logger,
    OdinConfiguration configuration,
    IDynamicHttpClientFactory httpClientFactory) : IMailRelayProvider
{
    private const string DkimSuffix = "_domainkey";

    private OdinConfiguration.RelaySection Relay => configuration.Email.Relay;

    public bool IsConfigured => Relay.Provider == OdinConfiguration.RelayProvider.Smtp2Go;

    public async Task<MailRelayDomainState?> GetDomainAsync(
        AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync("/domain/view", new { }, cancellationToken);
        var entry = response.Data.Domains.FirstOrDefault(
            x => string.Equals(x.Domain.FullDomain, domain.DomainName, StringComparison.OrdinalIgnoreCase));

        return entry == null ? null : ToState(domain, entry);
    }

    public async Task<MailRelayDomainState> EnsureDomainAsync(
        AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        // Look before adding. /domain/add is NOT idempotent - a re-add returns HTTP 400
        // "A sender domain matching the passed value of <domain> already exists" under the
        // generic error_code E_ApiResponseCodes.API_EXCEPTION. Since that code is shared with
        // every other failure, swallowing it would mean matching on English message text,
        // which breaks the first time they reword it. One extra read costs nothing and the
        // onboarding job retries, so this path runs more than once by design.
        var existing = await GetDomainAsync(domain, cancellationToken);
        if (existing != null)
        {
            logger.LogDebug("Relay: {domain} already registered", domain);
            return existing;
        }

        // auto_verify off: our DNS is not published yet at this point, so letting them verify
        // now would only record a failure. The job verifies once the records are written.
        var response = await PostAsync("/domain/add", new
        {
            domain = domain.DomainName,
            auto_verify = false,
        }, cancellationToken);

        var entry = response.Data.Domains.FirstOrDefault()
                    ?? throw new OdinSystemException($"Relay: /domain/add returned no domain for {domain}");

        logger.LogInformation("Relay: registered {domain} (dkim={dkim}, rpath={rpath})",
            domain, entry.Domain.DkimSelector, entry.Domain.RpathSelector);

        return ToState(domain, entry);
    }

    public async Task<MailRelayDomainState> VerifyDomainAsync(
        AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync("/domain/verify", new { domain = domain.DomainName }, cancellationToken);
        var entry = response.Data.Domains.FirstOrDefault()
                    ?? throw new OdinSystemException($"Relay: /domain/verify returned no domain for {domain}");

        var state = ToState(domain, entry);
        logger.LogInformation("Relay: verified {domain} -> {verified} ({problems} problem(s))",
            domain, state.Verified, state.Problems.Count);
        return state;
    }

    public async Task RemoveDomainAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        await PostAsync("/domain/remove", new { domain = domain.DomainName }, cancellationToken);
        logger.LogInformation("Relay: removed {domain}", domain);
    }

    /// <summary>
    /// Their shape to ours. The two records are built from the selectors the API allocates -
    /// never hard-coded - because they are per-domain and change if a domain is re-added.
    /// </summary>
    private MailRelayDomainState ToState(AsciiDomainName domain, Smtp2GoDomainEntry entry)
    {
        var d = entry.Domain;
        var records = new List<DnsConfig>();
        var problems = new List<string>();

        if (!string.IsNullOrEmpty(d.DkimSelector))
        {
            var name = $"{d.DkimSelector}.{DkimSuffix}";
            AssertNotOurDkimSelector(domain, d.DkimSelector);
            records.Add(new DnsConfig
            {
                Type = "CNAME",
                Name = name,
                Domain = $"{name}.{domain.DomainName}",
                Value = d.DkimValue,
                AltValue = d.DkimValue,
                Description = "Relay DKIM CNAME",
                Optional = true,
            });
            if (!d.DkimVerified && !string.IsNullOrWhiteSpace(d.DkimStatus))
            {
                problems.Add(d.DkimStatus);
            }
        }

        if (!string.IsNullOrEmpty(d.RpathSelector))
        {
            records.Add(new DnsConfig
            {
                Type = "CNAME",
                Name = d.RpathSelector,
                Domain = $"{d.RpathSelector}.{domain.DomainName}",
                Value = d.RpathValue,
                AltValue = d.RpathValue,
                Description = "Relay Return-Path CNAME (SPF)",
                Optional = true,
            });
            if (!d.RpathVerified && !string.IsNullOrWhiteSpace(d.RpathStatus))
            {
                problems.Add(d.RpathStatus);
            }
        }

        // Trackers are reported whether or not they are enabled, and /domain/verify probes
        // their hostname regardless - so an unverified DISABLED tracker is normal and must not
        // count against the domain. Only an enabled one is ours to publish or worry about.
        foreach (var tracker in entry.Trackers.Where(t => t.Enabled))
        {
            records.Add(new DnsConfig
            {
                Type = "CNAME",
                Name = tracker.FullDomain.Replace($".{domain.DomainName}", ""),
                Domain = tracker.FullDomain,
                Value = tracker.CnameValue,
                AltValue = tracker.CnameValue,
                Description = "Relay tracking CNAME",
                Optional = true,
            });
            if (!tracker.CnameVerified && !string.IsNullOrWhiteSpace(tracker.CnameStatus))
            {
                problems.Add(tracker.CnameStatus);
            }
        }

        var verified = d.DkimVerified
                       && d.RpathVerified
                       && entry.Trackers.Where(t => t.Enabled).All(t => t.CnameVerified);

        return new MailRelayDomainState
        {
            Domain = domain.DomainName,
            Records = records,
            Verified = verified,
            Problems = problems,
        };
    }

    /// <summary>
    /// The relay allocates selectors like "s934313", and the tenant's own DKIM lives at
    /// "s1"/"s2" - the same namespace. A collision would have us overwrite the tenant's signing
    /// key with a CNAME, silently breaking DKIM on every message Stalwart sends. It cannot
    /// happen with a six-digit allocation, which is exactly why it is worth asserting: nobody
    /// would notice the day it changed.
    /// </summary>
    private static void AssertNotOurDkimSelector(AsciiDomainName domain, string selector)
    {
        string[] ours = [DkimKeyGenerator.Ed25519Selector, DkimKeyGenerator.RsaSelector];
        if (ours.Contains(selector, StringComparer.OrdinalIgnoreCase))
        {
            throw new OdinSystemException(
                $"Relay returned DKIM selector '{selector}' for {domain}, which collides with " +
                "this tenant's own DKIM selector. Refusing to publish.");
        }
    }

    private async Task<Smtp2GoDomainResponse> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new OdinSystemException("Relay: Email:Relay:Provider is not Smtp2Go");
        }

        var httpClient = httpClientFactory.CreateClient($"{nameof(Smtp2GoRelayProvider)}");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Relay.ApiBaseUrl}{path}");
        request.Headers.Add("X-Smtp2go-Api-Key", Relay.ApiKey);
        request.Content = new StringContent(OdinSystemSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Their errors arrive as a 4xx with the detail in data.error. Surface that rather
            // than the status code alone, which on its own says nothing useful.
            var detail = TryReadError(content) ?? content;
            throw new OdinSystemException($"Relay: POST {path} returned {(int)response.StatusCode}: {detail}");
        }

        return OdinSystemSerializer.Deserialize<Smtp2GoDomainResponse>(content)
               ?? throw new OdinSystemException($"Relay: POST {path} returned unparseable content");
    }

    private static string? TryReadError(string content)
    {
        try
        {
            return OdinSystemSerializer.Deserialize<Smtp2GoDomainResponse>(content)?.Data.Error;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
