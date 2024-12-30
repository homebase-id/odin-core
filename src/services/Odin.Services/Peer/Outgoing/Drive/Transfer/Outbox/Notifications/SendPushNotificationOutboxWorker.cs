using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Serilog;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Notifications;

public class SendPushNotificationOutboxWorker(
    OutboxFileItem fileItem,
    ILogger<SendPushNotificationOutboxWorker> logger,
    IAppRegistrationService appRegistrationService,
    PushNotificationService pushNotificationService)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        await PerformanceCounter.MeasureExecutionTime("Notifications SendPushNotification",
            async () =>
            {
                var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                await PushItem(newContext, cancellationToken);
            });

        return (true, UnixTimeUtc.ZeroTime);
    }

    private async Task PushItem(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        //HACK as I refactor stuff - I should rather deserialize this in the push notification service?
        var data = fileItem.State.Data?.ToStringFromUtf8Bytes();
        if (string.IsNullOrEmpty(data))
        {
            logger.LogInformation("OutboxItemState.Data was null or empty; this is mostly likely due to an " +
                                  "old format push notification. (added timestamp (ms): {timestamp}.  Action: Marking Complete",
                fileItem.AddedTimestamp);
            return;
        }

        var record = OdinSystemSerializer.Deserialize<PushNotificationOutboxRecord>(data);

        if (record == null)
        {
            logger.LogInformation("OutboxItemState.Data was null or empty; this is mostly likely due to an " +
                                  "old format push notification. (added timestamp (ms): {timestamp}.  Action: Marking Complete",
                fileItem.AddedTimestamp);
            return;
        }

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
            //TODO: change to proper logger
            Log.Warning("No app registered with Id {id}", record.Options.AppId);
        }

        try
        {
            await pushNotificationService.PushAsync(pushContent, odinContext, cancellationToken);
        }
        catch (Exception e)
        {
            throw new OdinOutboxProcessingException(e.Message)
            {
                Recipient = default,
                TransferStatus = LatestTransferStatus.UnknownServerError,
                VersionTag = default
            };
        }
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