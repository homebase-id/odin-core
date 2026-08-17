using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;

namespace Odin.Hosting.Cli.Commands;

// Backfill of pre-provisioned own-domain zones (see docs/byod-dns-zone-plan.md).
//
//   create-own-domain-zones   create missing zones for all existing own-domain identities
//
// Zone deletion rides tenant deletion (odin-cli tenant delete -> DeleteTenantJob ->
// DeleteDnsRecordsForDomain); there is deliberately no orphan-sweeping prune command,
// because on a DNS server shared between environments "no registration here" does not
// mean "no registration anywhere".
//
public static class OwnDomainZones
{
    public static async Task CreateAsync(IServiceProvider services)
    {
        // Check config BEFORE resolving IIdentityRegistrationService: resolving it eagerly
        // constructs the PowerDNS client, whose Uri throws on an empty host address - the
        // very configuration this guard exists to handle gracefully
        var config = services.GetRequiredService<OdinConfiguration>();
        if (string.IsNullOrEmpty(config.Registry.PowerDnsApiKey) ||
            config.Registry.DnsConfigurationSet.NameServers.Count == 0 ||
            string.IsNullOrEmpty(config.Registry.PowerDnsHostAddress))
        {
            Console.WriteLine("Zone hosting is not configured (Registry:PowerDnsApiKey, " +
                              "Registry:PowerDnsHostAddress and/or Registry:DnsRecordValues:NameServers " +
                              "missing). Nothing to do.");
            return;
        }

        var regService = services.GetRequiredService<IIdentityRegistrationService>();
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
                var result = await regService.CreateOwnDomainZone(domain);
                if (result == CreateOwnDomainZoneResult.Created)
                {
                    created++;
                }
                else
                {
                    refused++;
                    Console.WriteLine($"REFUSED {domain}: {result}");
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

    private static bool IsManagedDomain(OdinConfiguration config, string domain)
    {
        return config.Registry.ManagedDomainApexes.Exists(x =>
            domain.Equals(x.Apex, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith("." + x.Apex, StringComparison.OrdinalIgnoreCase));
    }
}
