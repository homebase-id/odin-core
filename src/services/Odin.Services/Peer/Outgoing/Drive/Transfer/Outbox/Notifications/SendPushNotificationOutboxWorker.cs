using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Notifications;

public class SendPushNotificationOutboxWorker(
    OutboxItem item,
    IAppRegistrationService appRegistrationService,
    PushNotificationService pushNotificationService,
    ILogger<PeerOutboxProcessor> logger,
    IPeerOutbox peerOutbox)
{
    public async Task<OutboxProcessingResult> Send(IOdinContext odinContext)
    {
        try
        {
            //TODO: change to odinContext.Clone()
            using (new UpgradeToPeerTransferSecurityContext(odinContext))
            {
                var results = await this.PushItem(odinContext);
                await peerOutbox.MarkComplete(item.Marker);
            }
        }
        catch (OdinOutboxProcessingException)
        {
            // var nextRun = UnixTimeUtc.Now().AddSeconds(-5);
            // await peerOutbox.MarkFailure(item.Marker, nextRun);
            // we're not going to retry push notifications for now
            await peerOutbox.MarkComplete(item.Marker);
        }
        catch
        {
            await peerOutbox.MarkComplete(item.Marker);
        }

        return null;
    }


    private async Task<OutboxProcessingResult> PushItem(IOdinContext odinContext)
    {
        //HACK as I refactor stuff - i should rather deserialize this in the push notification service?
        var record = OdinSystemSerializer.Deserialize<PushNotificationOutboxRecord>(item.RawValue.ToStringFromUtf8Bytes());

        var pushContent = new PushNotificationContent()
        {
            Payloads = new List<PushNotificationPayload>()
        };

        var (validAppName, appName) = await TryResolveAppName(record.Options.AppId, odinContext);

        if (validAppName)
        {
            pushContent.Payloads.Add(new PushNotificationPayload()
            {
                AppDisplayName = appName,
                Options = record.Options,
                SenderId = record.SenderId,
                Timestamp = record.Timestamp,
            });
        }
        else
        {
            logger.LogWarning("No app registered with Id {id}", record.Options.AppId);
        }
        
        await pushNotificationService.Push(pushContent, odinContext);

        return new OutboxProcessingResult
        {
            Recipient = default,
            RecipientPeerResponseCode = null,
            TransferResult = TransferResult.Success,
            File = default,
            Timestamp = UnixTimeUtc.Now().milliseconds,
            OutboxItem = item
        };
    }

    private async Task<(bool success, string appName)> TryResolveAppName(Guid appId, IOdinContext odinContext)
    {
        if (appId == SystemAppConstants.OwnerAppId)
        {
            return (true, "Homebase Owner");
        }

        if (appId == SystemAppConstants.FeedAppId)
        {
            return (true, "Homebase Feed");
        }

        var appReg = await appRegistrationService.GetAppRegistration(appId, odinContext);
        return (appReg != null, appReg?.Name);
    }
}