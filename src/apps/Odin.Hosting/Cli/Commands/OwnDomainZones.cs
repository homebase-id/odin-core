using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.Dns;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;

namespace Odin.Hosting.Cli.Commands;

// Backfill/cleanup of pre-provisioned own-domain zones (see docs/byod-dns-zone-plan.md).
//
//   create-own-domain-zones          create missing zones for all existing own-domain identities
//   prune-own-domain-zones [commit]  delete zones with no matching identity registration;
//                                    dry-run unless "commit" is passed
//
public static class OwnDomainZones
{
    public static async Task CreateAsync(IServiceProvider services)
    {
        var regService = services.GetRequiredService<IIdentityRegistrationService>();
        if (!regService.CanHostOwnDomainZones)
        {
            Console.WriteLine("Zone hosting is not configured (Registry:PowerDnsApiKey and/or " +
                              "Registry:DnsRecordValues:NameServers missing). Nothing to do.");
            return;
        }

        var config = services.GetRequiredService<OdinConfiguration>();
        var registry = services.GetRequiredService<IIdentityRegistry>();
        await registry.LoadRegistrations();
        var tenants = await registry.GetTenants();

        var created = 0;
        var refused = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var tenant in tenants)
        {
            var domain = tenant.PrimaryDomainName;
            if (IsManagedDomain(config, domain))
            {
                skipped++;
                continue;
            }

            try
            {
                // Registered identities always pass the domain-control gate; a refusal here
                // means the shadow guard hit (domain inside a zone we host)
                if (await regService.CreateOwnDomainZone(domain))
                {
                    created++;
                }
                else
                {
                    refused++;
                    Console.WriteLine($"REFUSED {domain} (see log)");
                }
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"FAILED {domain}: {e.Message}");
            }
        }

        Console.WriteLine($"Done. Zones ensured: {created}, refused: {refused}, managed domains skipped: {skipped}, failed: {failed}");
    }

    //

    public static async Task PruneAsync(IServiceProvider services, bool commit)
    {
        var regService = services.GetRequiredService<IIdentityRegistrationService>();
        if (!regService.CanHostOwnDomainZones)
        {
            Console.WriteLine("Zone hosting is not configured. Nothing to do.");
            return;
        }

        var config = services.GetRequiredService<OdinConfiguration>();
        var dnsRestClient = services.GetRequiredService<IDnsRestClient>();
        var registry = services.GetRequiredService<IIdentityRegistry>();
        await registry.LoadRegistrations();
        var tenants = await registry.GetTenants();
        var registeredDomains = tenants
            .Select(x => x.PrimaryDomainName.ToLower())
            .ToHashSet();

        var ourNameServers = config.Registry.DnsConfigurationSet.NameServers
            .Select(x => x.ToLower())
            .ToHashSet();

        var zones = await dnsRestClient.GetZones();
        var deleted = 0;
        var kept = 0;
        foreach (var zone in zones)
        {
            var domain = zone.name.TrimEnd('.').ToLower();

            // Never touch zones we did not create per-identity: managed apex zones and
            // anything that is not a syntactically valid identity domain.
            if (IsManagedDomain(config, domain) ||
                domain == config.Registry.ProvisioningDomain ||
                !AsciiDomainNameValidator.TryValidateDomain(domain) ||
                registeredDomains.Contains(domain))
            {
                kept++;
                continue;
            }

            // Only zones whose apex NS set is exactly our configured nameservers are
            // candidates - anything else (infra zones, hand-made zones) is left alone.
            var zoneWithRecords = await dnsRestClient.GetZone(zone.name);
            var zoneNameServers = (zoneWithRecords.rrsets ?? [])
                .Where(x => x.type == "NS" && x.name == zone.name)
                .SelectMany(x => x.records)
                .Select(x => x.content.TrimEnd('.').ToLower())
                .ToHashSet();
            if (!zoneNameServers.SetEquals(ourNameServers))
            {
                Console.WriteLine($"Keeping {zone.name}: no registration, but NS set is not ours");
                kept++;
                continue;
            }

            deleted++;
            if (commit)
            {
                Console.WriteLine($"Deleting orphan zone {zone.name}");
                await dnsRestClient.DeleteZone(zone.name);
            }
            else
            {
                Console.WriteLine($"Would delete orphan zone {zone.name} (dry-run; pass 'commit' to apply)");
            }
        }

        Console.WriteLine($"Done. Orphans {(commit ? "deleted" : "found")}: {deleted}, zones kept: {kept}");
    }

    //

    private static bool IsManagedDomain(OdinConfiguration config, string domain)
    {
        return config.Registry.ManagedDomainApexes.Exists(x =>
            domain.Equals(x.Apex, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith("." + x.Apex, StringComparison.OrdinalIgnoreCase));
    }
}
