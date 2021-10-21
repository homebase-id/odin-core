using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit
{
    public class TransitService : DotYouServiceBase, ITransitService
    {
        private class Envelope
        {
            public string Recipient { get; set; }
            public TransferSpec TransferSpec { get; set; }
        }


        private readonly IStorageService _storageService;
        private readonly IOutboxQueueService _outboxQueue;
        const int InstantSendPayloadThresholdSize = 1024;
        const int InstantSendRecipientCountThreshold = 9;

        public TransitService(DotYouContext context, ILogger logger, IOutboxQueueService outboxQueue, IStorageService storageService, IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac ) : base(context, logger, notificationHub, fac)
        {
            _outboxQueue = outboxQueue;
            _storageService = storageService;
        }

        public async Task<TransferResult> SendBatchNow(IEnumerable<TransferQueueItem> queuedItems)
        {
            var envelopes = queuedItems.Select(i => new Envelope()
            {
                Recipient = i.Recipient,
                TransferSpec = i.TransferSpec
            });

            return await SendBatchNow(envelopes);
        }

        public async Task<TransferResult> SendBatchNow(RecipientList recipients, TransferSpec spec)
        {
            var envelopes = recipients.Recipients.Select(r => new Envelope()
            {
                Recipient = r,
                TransferSpec = spec
            });

            return await SendBatchNow(envelopes);
        }


        public async Task<TransferResult> StartDataTransfer(RecipientList recipients, TransferSpec spec)
        {
            if (spec.Id == Guid.Empty)
            {
                throw new Exception("Invalid transfer id");
            }

            //if payload size is small, try sending now.
            if (new FileInfo(spec.File.DataFilePath).Length <= InstantSendPayloadThresholdSize || recipients.Recipients.Length < InstantSendRecipientCountThreshold)
            {
                Console.WriteLine("Length is small, sending now");
                return await this.SendBatchNow(recipients, spec);
            }

            var result = new TransferResult(spec.Id);
            Console.WriteLine("Data file is large, putting in queue");
            foreach (var recipient in recipients.Recipients)
            {
                _outboxQueue.Enqueue(new TransferQueueItem()
                {
                    Recipient = recipient,
                    TransferSpec = spec
                });

                result.QueuedRecipients.Add(recipient);
            }

            return result;
        }

        public async Task<SendResult> SendNow(string recipient, TransferSpec spec)
        {
            return await this.SendAsync(recipient, spec);
        }

        private async Task<TransferResult> SendBatchNow(IEnumerable<Envelope> envelopes)
        {
            var result = new TransferResult(Guid.NewGuid());
            var tasks = new List<Task<SendResult>>();

            foreach (var envelope in envelopes)
            {
                tasks.Add(SendAsync(envelope.Recipient, envelope.TransferSpec));
            }

            await Task.WhenAll(tasks);

            //build results
            tasks.ForEach(task =>
            {
                var sendResult = task.Result;
                if (sendResult.Success)
                {
                    result.SuccessfulRecipients.Add(sendResult.Recipient);
                }
                else
                {
                    var item = new TransferQueueItem()
                    {
                        Recipient = sendResult.Recipient,
                        TransferSpec = sendResult.TransferSpec,
                    };

                    _outboxQueue.EnqueueFailure(item, sendResult.FailureReason.GetValueOrDefault());

                    result.QueuedRecipients.Add(sendResult.Recipient);
                }
            });

            return result;
        }


        private async Task<SendResult> SendAsync(string recipient, TransferSpec spec)
        {
            RecipientDataTransferProcess proc = new RecipientDataTransferProcess(null, recipient, spec, null);
            var sendResult = await proc.Run();
            return sendResult;
        }
    }
}