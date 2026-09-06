using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Services.Background.BackgroundServices.System;

/// <summary>
/// Periodically reconciles this node's in-memory identity registry against the database.
/// </summary>
/// <remarks>
/// Registry changes are announced over pub/sub, but that delivery is at-most-once: a node that was
/// starting up, paused, or briefly unable to reach Redis would otherwise keep serving a stale view
/// of a tenant until it restarted. This sweep is what bounds that staleness, and it is why
/// disabling a tenant can be relied on as an operational kill switch across the cluster.
/// </remarks>
public class RegistryReconciliationBackgroundService(
    ILogger<RegistryReconciliationBackgroundService> logger,
    OdinConfiguration odinConfig,
    IIdentityRegistry registry)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(odinConfig.BackgroundServices.RegistryReconciliationIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (registry is FileSystemIdentityRegistry fileSystemRegistry)
                {
                    var changes = await fileSystemRegistry.ReconcileWithDatabaseAsync();
                    if (changes > 0)
                    {
                        logger.LogInformation("Registry reconciliation applied {changes} change(s)", changes);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Registry reconciliation failed: {error}", e.Message);
            }

            await SleepAsync(interval, stoppingToken);
        }
    }
}
