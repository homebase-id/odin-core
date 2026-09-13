using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;

namespace Odin.Hosting.Cli.Commands;

// Backfill of managed-domain records in the shared apex zones (the managed-domain
// counterpart of create-own-domain-zones; own-domain zones re-populate through that
// command, managed domains had no equivalent until the email records made one necessary,
// see docs/email-dns-plan.md).
//
//   populate-managed-domain-records          dry-run: list what would be ensured, change nothing
//   populate-managed-domain-records commit   (re)write records for all managed-domain identities
//
public static class ManagedDomainRecords
{
    public static async Task PopulateAsync(IServiceProvider services, bool commit)
    {
        // Check config BEFORE resolving IIdentityRegistrationService: resolving it eagerly
        // constructs the PowerDNS client, whose Uri throws on an empty host address
        var config = services.GetRequiredService<OdinConfiguration>();
        if (string.IsNullOrEmpty(config.Registry.PowerDnsApiKey) ||
            string.IsNullOrEmpty(config.Registry.PowerDnsHostAddress))
        {
            Console.WriteLine("PowerDNS is not configured (Registry:PowerDnsApiKey and/or " +
                              "Registry:PowerDnsHostAddress missing). Nothing to do.");
            return;
        }

        if (!config.Email.TenantMail.Enabled)
        {
            Console.WriteLine("Note: Email:TenantMail is disabled - only the base records " +
                              "(A/CNAME) are (re)written; email records appear once the flag is on.");
        }

        var regService = services.GetRequiredService<IIdentityRegistrationService>();
        var registry = services.GetRequiredService<IIdentityRegistry>();
        await registry.LoadRegistrations();
        var tenants = await registry.GetTenants();

        if (!commit)
        {
            Console.WriteLine("Dry-run - no changes are made. Pass 'commit' to apply.");
        }

        var ensured = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var tenant in tenants)
        {
            var domain = tenant.PrimaryDomainName;

            // Longest matching apex wins, in case one managed apex is a suffix of another
            var apex = config.Registry.ManagedDomainApexes
                .Where(x => domain.EndsWith("." + x.Apex, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Apex.Length)
                .FirstOrDefault()?.Apex;
            if (apex == null)
            {
                skipped++; // own-domain identity; create-own-domain-zones covers it
                continue;
            }

            try
            {
                var prefix = domain[..^(apex.Length + 1)];
                if (!commit)
                {
                    ensured++;
                    Console.WriteLine($"WOULD ENSURE {domain}");
                    continue;
                }

                await regService.EnsureManagedDomainRecords(prefix, apex);
                ensured++;
                Console.WriteLine($"ENSURED      {domain}");
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"FAILED       {domain}: {e.Message}");
            }
        }

        Console.WriteLine(commit
            ? $"Done. Records ensured: {ensured}, own-domains skipped: {skipped}, failed: {failed}"
            : $"Dry-run done. Would ensure: {ensured}, own-domains skipped: {skipped}, failed: {failed}");
    }
}
