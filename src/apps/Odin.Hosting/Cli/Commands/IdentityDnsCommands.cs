using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.System;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.Dns;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;

namespace Odin.Hosting.Cli.Commands;

#nullable enable

/// <summary>
/// One identity's DNS, as opposed to the host-wide populate-managed-domain-records and create-own-domain-zones, which
/// rewrite every identity on the host. Deleting a tenant never touches DNS; these do, on purpose.
/// </summary>
internal static class IdentityDnsCommands
{
    /// <summary>
    /// Points one identity's DNS at this host, at a chosen TTL. Only an identity this host serves: after a move the source
    /// has it disabled, so running this there cannot point it back.
    /// </summary>
    public static async Task<bool> RepointAsync(IServiceProvider services, string domain, int ttl, bool commit)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        if (!PowerDnsConfigured(services, logger))
        {
            return false;
        }

        var registration = await IdentityJsonTransfer.ReadRegistrationAsync(services.GetRequiredService<SystemDatabase>(), domain);
        if (registration == null)
        {
            logger.LogError("Refusing: {domain} is not registered on this host. Run this on the host that should serve it", domain);
            return false;
        }

        if (registration.Status == TenantStatus.Disabled)
        {
            logger.LogError("Refusing: {domain} is disabled on this host ({reason}), so this host no longer serves it",
                domain, registration.DisabledReason);
            return false;
        }

        List<IdentityDnsChange> changes;
        try
        {
            changes = await services.GetRequiredService<IIdentityRegistrationService>()
                .RepointIdentityDnsAsync(new AsciiDomainName(domain), ttl, commit);
        }
        catch (OdinSystemException e)
        {
            logger.LogError("Refusing: {message}", e.Message);
            return false;
        }

        foreach (var change in changes)
        {
            Console.WriteLine($"{(change.Changes ? "CHANGE" : "SAME  ")}  {change.Desired.Type,-5}  {change.Desired.Name}");
            if (change.Changes)
            {
                Console.WriteLine($"          now:  {Show(change.Current)}");
                Console.WriteLine($"          new:  {Show(change.Desired)}");
            }
        }

        var changed = changes.Count(change => change.Changes);
        Console.WriteLine(commit
            ? $"Done. {changed} of {changes.Count} rrset(s) of {domain} changed."
            : $"Dry run. {changed} of {changes.Count} rrset(s) of {domain} would change. Pass 'commit' to apply.");
        return true;
    }

    /// <summary>
    /// Deletes one identity's DNS: once it is deleted from this host, and only DNS that points here. After a move that is
    /// the host it moved to; on the source the command refuses, since the records are the target's.
    /// </summary>
    public static async Task<bool> DeleteAsync(IServiceProvider services, string domain, bool commit)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        if (!PowerDnsConfigured(services, logger))
        {
            return false;
        }

        // Still registered, even paused or disabled, it may be mid-move or rolled back to: delete the tenant first
        if (await IdentityJsonTransfer.ReadRegistrationAsync(services.GetRequiredService<SystemDatabase>(), domain) is { } registration)
        {
            logger.LogError("Refusing: {domain} is still registered on this host ({status}). Delete the tenant first", domain,
                registration.Status);
            return false;
        }

        List<DnsRrset> rrsets;
        try
        {
            rrsets = await services.GetRequiredService<IIdentityRegistrationService>()
                .DeleteIdentityDnsAsync(new AsciiDomainName(domain), commit);
        }
        catch (OdinSystemException e)
        {
            logger.LogError("Refusing: {message}", e.Message);
            return false;
        }

        foreach (var rrset in rrsets)
        {
            Console.WriteLine($"DELETE  {rrset.Type,-5}  {rrset.Name}  {Show(rrset)}");
        }

        Console.WriteLine("Also its DKIM TXTs if it has any, and for an own domain the whole zone.");
        Console.WriteLine(commit
            ? $"Done. {domain}'s DNS is deleted."
            : $"Dry run. Pass 'commit' to delete {domain}'s DNS.");
        return true;
    }

    // Before resolving the registration service: resolving it constructs the PowerDNS client, whose Uri throws on an
    // empty host address
    private static bool PowerDnsConfigured(IServiceProvider services, ILogger logger)
    {
        var config = services.GetRequiredService<OdinConfiguration>();
        if (string.IsNullOrEmpty(config.Registry.PowerDnsApiKey) || string.IsNullOrEmpty(config.Registry.PowerDnsHostAddress))
        {
            logger.LogError("PowerDNS is not configured (Registry:PowerDnsApiKey, Registry:PowerDnsHostAddress)");
            return false;
        }

        return true;
    }

    private static string Show(DnsRrset? rrset) =>
        rrset == null ? "(none)" : $"{string.Join(" | ", rrset.Contents)}  (ttl {rrset.Ttl})";
}
