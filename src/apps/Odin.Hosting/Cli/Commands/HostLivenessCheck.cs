using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using Odin.Services.Configuration;

namespace Odin.Hosting.Cli.Commands;

#nullable enable

// Detects a host already serving this configuration.
//
// STEP 1 ONLY. This exists because export and import must not run while anything is
// writing to the identity, and today the only way to guarantee that is a stopped host.
// It is a guard against the obvious mistake, not a safety property:
//
//   - It sees this machine's TCP listeners. A second host on another machine, sharing
//     the same Postgres, is invisible to it and will keep writing throughout.
//   - A host that has crashed but left workers draining, or one starting up between the
//     probe and the export, both slip through.
//
// The real fix is a tenant lifecycle state that workers observe, plus a freeze
// acknowledgement across hosts, which then allows export while the hosts are running.
// See the withdrawn Task 10 in the plan and the spec's freeze section.
public static class HostLivenessCheck
{
    // Returns the configured ports that something is currently listening on. Empty means
    // no host was detected here, which is weaker than "no host is running".
    public static List<int> FindListeningPorts(OdinConfiguration config)
    {
        var configured = new HashSet<int>();

        foreach (var entry in config.Host.IpAddressListenList)
        {
            if (entry.HttpPort > 0) configured.Add(entry.HttpPort);
            if (entry.HttpsPort > 0) configured.Add(entry.HttpsPort);
        }

        // An empty listen list still means the host binds its defaults.
        if (configured.Count == 0)
        {
            configured.Add(config.Host.DefaultHttpPort);
            configured.Add(config.Host.DefaultHttpsPort);
        }

        // Read-only: reads the OS listener table rather than trying to bind, so it cannot
        // steal a port from a host that is starting up.
        var active = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(e => e.Port)
            .ToHashSet();

        return configured.Where(active.Contains).OrderBy(p => p).ToList();
    }
}
