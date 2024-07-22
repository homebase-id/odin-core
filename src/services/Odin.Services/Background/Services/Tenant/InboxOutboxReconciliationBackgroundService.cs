using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.Background.Services.Tenant;

public class InboxOutboxReconciliationBackgroundService(
    ILogger<InboxOutboxReconciliationBackgroundService> logger,
    OdinConfiguration config,
    TenantSystemStorage tenantSystemStorage,
    TransitInboxBoxStorage inbox,
    PeerOutbox outbox,
    PeerOutboxProcessorBackgroundService outboxProcessor)
    : AbstractBackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Reconciling inbox and outbox");
            
            var ageSeconds = config.Host.InboxOutboxRecoveryAgeSeconds;
            var time = UnixTimeUtc.FromDateTime(DateTime.Now.Subtract(TimeSpan.FromSeconds(ageSeconds)));

            var recoveredOutboxItems = 0;
            var recoveredInboxItems = 0;
            using (var cn = tenantSystemStorage.CreateConnection())
            {
                recoveredOutboxItems = await outbox.RecoverDead(time, cn);
                recoveredInboxItems = await inbox.RecoverDead(time, cn);
            }

            if (recoveredOutboxItems > 0)
            {
                logger.LogInformation("Recovered {count} outbox items", recoveredOutboxItems);
                outboxProcessor.WakeUp(); // signal outbox processor to get to work                
            }

            if (recoveredInboxItems > 0)
            {
                logger.LogInformation("Recovered {count} inbox items", recoveredOutboxItems);
            }
            
            await SleepAsync(TimeSpan.FromSeconds(config.Job.InboxOutboxReconciliationDelaySeconds), stoppingToken);
        }
    }
}
