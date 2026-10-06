using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Services.Background.BackgroundServices.System;

/// <summary>
/// Makes every node apply registry changes it was never told about. Announcements are the fast
/// path; this is the safety net that bounds how long one node can serve an identity the others have
/// paused to <see cref="OdinConfiguration.RegistrySection.CatchUpIntervalSeconds"/>.
/// </summary>
public sealed class RegistryCatchUpBackgroundService(
    ILogger<RegistryCatchUpBackgroundService> logger,
    OdinConfiguration config,
    IIdentityRegistry registry)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(config.Registry.CatchUpIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SleepAsync(interval, stoppingToken);
            try
            {
                await registry.CatchUpAsync();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Registry catch-up failed; retrying in {interval}: {error}", interval, e.Message);
            }
        }
    }
}
