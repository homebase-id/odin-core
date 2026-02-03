using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.Background.BackgroundServices.Tenant;

public class InboxOutboxReconciliationBackgroundService(
    ILogger<InboxOutboxReconciliationBackgroundService> logger,
    OdinConfiguration config,
    TransitInboxBoxStorage inbox,
    PeerOutbox outbox,
    IBackgroundServiceNotifier<PeerOutboxProcessorBackgroundService> backgroundServiceNotifier)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{service} is running", GetType().Name);
            
            var ageSeconds = config.Host.InboxOutboxRecoveryAgeSeconds;
            var time = UnixTimeUtc.FromDateTime(DateTime.Now.Subtract(TimeSpan.FromSeconds(ageSeconds)));

            var recoveredOutboxItems = 0;
            var recoveredInboxItems = 0;

            recoveredOutboxItems = await outbox.RecoverDeadAsync(time);
            recoveredInboxItems = await inbox.RecoverDeadAsync(time);

            if (recoveredOutboxItems > 0)
            {
                logger.LogInformation("Recovered {count} outbox items", recoveredOutboxItems);
                await backgroundServiceNotifier.NotifyWorkAvailableAsync(); // signal outbox processor to get to work
            }

            if (recoveredInboxItems > 0)
            {
                logger.LogInformation("Recovered {count} inbox items", recoveredOutboxItems);
            }

            var interval = TimeSpan.FromSeconds(config.BackgroundServices.InboxOutboxReconciliationIntervalSeconds);
            logger.LogDebug("{service} is sleeping for {SleepDuration}", GetType().Name, interval);
            await SleepAsync(interval, stoppingToken);
        }
    }
}
