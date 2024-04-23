using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Notifications;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class PeerOutboxProcessor(
        IPeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        ILogger<PeerOutboxProcessor> logger,
        PushNotificationService pushNotificationService,
        IAppRegistrationService appRegistrationService,
        FileSystemResolver fileSystemResolver)
    {
        public async Task StartOutboxProcessing(IOdinContext odinContext)
        {
            var item = await peerOutbox.GetNextItem();

            while (item != null)
            {
                // _ = this.ProcessItem(item, odinContext);
                await this.ProcessItem(item, odinContext);
                item = await peerOutbox.GetNextItem();
            }
        }

        public async Task<List<OutboxProcessingResult>> ProcessItemsSync(IEnumerable<OutboxItem> items, IOdinContext odinContext)
        {
            var sendFileTasks = new List<Task<OutboxProcessingResult>>();
            var results = new List<OutboxProcessingResult>();

            sendFileTasks.AddRange(items.Select(i => ProcessItem(i, odinContext)));

            await Task.WhenAll(sendFileTasks);

            var filesForDeletion = new List<OutboxItem>();
        
            sendFileTasks.ForEach(task => 
            {
                var sendResult = task.Result;
                results.Add(sendResult);

                if (sendResult.TransferResult == TransferResult.Success)
                {
                    if (sendResult.OutboxItem.IsTransientFile)
                    {
                        filesForDeletion.Add(sendResult.OutboxItem);
                    }

                    peerOutbox.MarkComplete(sendResult.OutboxItem.Marker);
                }
                else
                {
                    var nextRun = UnixTimeUtc.Now().AddSeconds(-5);
                    peerOutbox.MarkFailure(sendResult.OutboxItem.Marker, nextRun);
                }
            });
                
            //
            // TODO: Here i need to see if the file is ready to be deleted; it might be stuck in the outbox.
            //
            
            //TODO: optimization point; I need to see if this sort of deletion code is needed anymore; now that we have the transient temp drive
            foreach (var item in filesForDeletion)
            {
                var fs = fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);
                await fs.Storage.HardDeleteLongTermFile(item.File, odinContext);
            }

            return results;
        }

        private async Task<OutboxProcessingResult> ProcessItem(OutboxItem item, IOdinContext odinContext)
        {
            //TODO: add benchmark
            logger.LogDebug("Processing outbox item type: {type}", item.Type);

            switch (item.Type)
            {
                case OutboxItemType.PushNotification:
                    return await SendPushNotification(item, odinContext);

                case OutboxItemType.File:
                    return await SendFileOutboxItem(item, odinContext);

                // case OutboxItemType.Reaction:
                //     return await SendReactionItem(item, odinContext);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task<OutboxProcessingResult> SendFileOutboxItem(OutboxItem item, IOdinContext odinContext)
        {
            var fs = fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);

            var worker = new SendFileOutboxWorker(item,
                fs,
                logger,
                peerOutbox,
                odinConfiguration,
                odinHttpClientFactory);

            return await worker.Send(odinContext);
        }

        private async Task<OutboxProcessingResult> SendPushNotification(OutboxItem item, IOdinContext odinContext)
        {
            var worker = new SendPushNotificationOutboxWorker(item,
                appRegistrationService,
                pushNotificationService,
                logger,
                peerOutbox);

            return await worker.Send(odinContext);
        }
    }
}