using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Serilog;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Notifications;

public class SendPushNotificationOutboxWorker(
    OutboxFileItem fileItem,
    IAppRegistrationService appRegistrationService,
    PushNotificationService pushNotificationService,
    PeerOutbox peerOutbox)
{
    public async Task Send(IOdinContext odinContext, DatabaseConnection cn, CancellationToken cancellationToken)
    {
        try
        {
            var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
            await PushItem(newContext, cn, cancellationToken);
            await peerOutbox.MarkComplete(fileItem.Marker, cn);
        }
        catch (OdinOutboxProcessingException)
        {
            // var nextRun = UnixTimeUtc.Now().AddSeconds(-5);
            // await peerOutbox.MarkFailure(item.Marker, nextRun);
            // we're not going to retry push notifications for now
            await peerOutbox.MarkComplete(fileItem.Marker, cn);
        }
        catch
        {
            await peerOutbox.MarkComplete(fileItem.Marker, cn);
        }
    }


    private async Task PushItem(IOdinContext odinContext, DatabaseConnection cn, CancellationToken cancellationToken)
    {
        //HACK as I refactor stuff - i should rather deserialize this in the push notification service?
        var record = OdinSystemSerializer.Deserialize<PushNotificationOutboxRecord>(fileItem.RawValue.ToStringFromUtf8Bytes());

        var pushContent = new PushNotificationContent()
        {
            Payloads = new List<PushNotificationPayload>()
        };

        var (validAppName, appName) = await TryResolveAppName(record.Options.AppId, odinContext, cn);

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
            await pushNotificationService.Push(pushContent, odinContext, cn, cancellationToken);
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

    private async Task<(bool success, string appName)> TryResolveAppName(Guid appId, IOdinContext odinContext, DatabaseConnection cn)
    {
        if (appId == SystemAppConstants.OwnerAppId)
        {
            return (true, "Homebase Owner");
        }

        if (appId == SystemAppConstants.FeedAppId)
        {
            return (true, "Homebase Feed");
        }

        var appReg = await appRegistrationService.GetAppRegistration(appId, odinContext, cn);
        return (appReg != null, appReg?.Name);
    }
}