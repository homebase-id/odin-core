using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Odin.Core.Dns;
using Odin.Services.Configuration;
using Odin.Services.Email;

#nullable enable

namespace Odin.Services.Dns.Health;

/// <summary>
/// Global DNS infrastructure checks run by StartupVerificationBackgroundService: the
/// hostname identities alias/CNAME to, the managed domain apexes offered at signup,
/// and (once tenant mail is enabled) the MX nodes. DNSSEC validation follows CNAME
/// chains, so an unsigned host zone caps every manual-records tenant's chain of trust
/// (docs/byod-dnssec-plan.md), and a managed-domain tenant's DNSSEC is entirely
/// Inherited from its apex zone - an unanchored apex silently voids it for every
/// tenant under it. Drift here is invisible until it bites, hence a boot-time check.
/// Generic public-DNS lookups only - never the PowerDNS API.
/// </summary>
public class DnsInfraVerifier(
    ILogger<DnsInfraVerifier> logger,
    OdinConfiguration configuration,
    ILookupClient dnsClient,
    IAuthoritativeDnsLookup authoritativeDnsLookup,
    IDnssecLookup dnssecLookup)
{
    public sealed class Result
    {
        /// <summary>Actively broken (e.g. SERVFAIL for validating resolvers, hostname unresolvable)</summary>
        public List<string> Errors { get; } = [];

        /// <summary>Not broken today, but caps tenants' DNSSEC or blocks DANE later</summary>
        public List<string> Warnings { get; } = [];

        public bool IsClean => Errors.Count == 0 && Warnings.Count == 0;
    }

    /// <summary>False when configuration gives this verifier nothing to check.</summary>
    public bool HasChecks =>
        !string.IsNullOrWhiteSpace(configuration.Registry.DnsConfigurationSet.ApexAliasRecord) ||
        configuration.Registry.ManagedDomainApexes.Count > 0 ||
        MxNodesToCheck.Count > 0;

    private List<string> MxNodesToCheck =>
        configuration.Email.TenantMail.Enabled && configuration.Email.Provider != EmailProvider.None
            ? configuration.Email.TenantMail.MxNodes
            : [];

    /// <summary>
    /// Runs all checks once and returns the findings. The caller retries with backoff and
    /// logs only after the final attempt, so a boot-time DNS blip does not false-alarm.
    /// </summary>
    public async Task<Result> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var result = new Result();

        // (hostname, description) pairs whose enclosing zones need DNSSEC anchoring
        var hosts = new List<(string Host, string Description)>();

        var alias = configuration.Registry.DnsConfigurationSet.ApexAliasRecord.Trim().TrimEnd('.').ToLowerInvariant();
        if (alias != "")
        {
            hosts.Add((alias, $"server hostname '{alias}'"));
            await VerifyServerHostnameResolutionAsync(alias, result, cancellationToken);
        }

        // Tenants under a managed apex are not zone cuts of their own - their DNSSEC
        // status is Inherited from the apex zone, so the apex is what must be anchored
        foreach (var apex in configuration.Registry.ManagedDomainApexes)
        {
            var domain = apex.Apex.Trim().TrimEnd('.').ToLowerInvariant();
            if (domain != "")
            {
                hosts.Add((domain, $"managed domain apex '{domain}'"));
            }
        }

        foreach (var node in MxNodesToCheck)
        {
            var mxNode = node.Trim().TrimEnd('.').ToLowerInvariant();
            hosts.Add((mxNode, $"MX node '{mxNode}'"));
        }

        // Anchoring is a property of the enclosing ZONE; hosts typically share one
        // (server + MX nodes under the infra domain), so evaluate each zone once
        var zones = new Dictionary<string, List<string>>();
        foreach (var (host, description) in hosts)
        {
            string zone;
            try
            {
                zone = await authoritativeDnsLookup.LookupZoneApexAsync(host, cancellationToken);
            }
            catch (Exception e)
            {
                result.Warnings.Add($"Could not determine the enclosing zone of {description}: {e.Message}");
                continue;
            }
            if (string.IsNullOrEmpty(zone))
            {
                result.Warnings.Add($"Could not determine the enclosing zone of {description}");
                continue;
            }
            zones.TryAdd(zone, []);
            zones[zone].Add(description);
        }

        foreach (var (zone, descriptions) in zones)
        {
            await VerifyZoneAnchoringAsync(zone, string.Join(", ", descriptions), result, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// The alias target must itself be a plain A record: a CNAME chain adds more zones
    /// the tenants' DNSSEC chain depends on (the michael.seifert.page lesson), and its
    /// address should agree with the apex A identities publish - drift here means new
    /// signups and alias users land on different hosts.
    /// </summary>
    private async Task VerifyServerHostnameResolutionAsync(string alias, Result result, CancellationToken cancellationToken)
    {
        try
        {
            var response = await dnsClient.QueryAsync(alias, QueryType.A, cancellationToken: cancellationToken);

            if (response.Answers.CnameRecords().Any())
            {
                result.Warnings.Add(
                    $"Server hostname '{alias}' resolves via a CNAME; every zone in the chain then caps DNSSEC - use a direct A record");
            }

            var addresses = response.Answers.ARecords().Select(x => x.Address.ToString()).ToList();
            var apexARecord = configuration.Registry.DnsConfigurationSet.ApexARecord;
            if (addresses.Count == 0)
            {
                result.Errors.Add($"Server hostname '{alias}' does not resolve to an A record");
            }
            else if (!string.IsNullOrEmpty(apexARecord) && !addresses.Contains(apexARecord))
            {
                result.Warnings.Add(
                    $"Server hostname '{alias}' resolves to [{string.Join(", ", addresses)}] which does not include the configured apex A record {apexARecord}");
            }
        }
        catch (Exception e)
        {
            result.Errors.Add($"Server hostname '{alias}' A record lookup failed: {e.Message}");
        }
    }

    private async Task VerifyZoneAnchoringAsync(string zone, string hostDescriptions, Result result, CancellationToken cancellationToken)
    {
        try
        {
            var dnsKeys = await dnssecLookup.GetZoneDnsKeysAsync(zone, cancellationToken);
            if (dnsKeys.Count == 0)
            {
                result.Warnings.Add(
                    $"Zone '{zone}' ({hostDescriptions}) is not DNSSEC-signed; tenant chains of trust are capped here and DANE is impossible - see docs/byod-dnssec-plan.md");
                return;
            }

            var parentZoneSigned = await dnssecLookup.IsParentZoneSignedAsync(zone, cancellationToken);
            var parentDsRecords = await dnssecLookup.GetParentDsRecordsAsync(zone, cancellationToken);
            var anyDsMatches = DnsHealthService.AnyPublishedDsMatchesZoneKeys(zone, dnsKeys, parentDsRecords);

            switch (DnsHealthService.ComputeVerdict(parentZoneSigned, parentDsRecords.Count, anyDsMatches))
            {
                case DnsHealthDnssecStatus.Secure:
                    logger.LogDebug("Zone {zone} ({hosts}) is DNSSEC-anchored", zone, hostDescriptions);
                    break;
                case DnsHealthDnssecStatus.DsMismatch:
                    result.Errors.Add(
                        $"Zone '{zone}' ({hostDescriptions}): no DS record at the parent matches the zone's keys - validating resolvers will SERVFAIL");
                    break;
                case DnsHealthDnssecStatus.ParentUnsigned:
                    result.Warnings.Add(
                        $"Zone '{zone}' ({hostDescriptions}) is signed but its parent zone is not - the chain of trust cannot extend here");
                    break;
                case DnsHealthDnssecStatus.DsMissing:
                    result.Warnings.Add(
                        $"Zone '{zone}' ({hostDescriptions}) is signed but no DS record is published at the parent - the zone is not anchored");
                    break;
            }
        }
        catch (Exception e)
        {
            result.Warnings.Add($"DNSSEC evaluation of zone '{zone}' ({hostDescriptions}) failed: {e.Message}");
        }
    }
}
