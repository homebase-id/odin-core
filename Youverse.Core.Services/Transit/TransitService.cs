using Refit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Profile;
using Youverse.Core.Services.Storage;
using Youverse.Core.Services.Transit.Audit;

namespace Youverse.Core.Services.Transit
{
    public class TransitService : TransitServiceBase, ITransitService
    {
        private class Envelope
        {
            public string Recipient { get; set; }
            public Guid FileId { get; set; }
        }

        private readonly IStorageService _storage;
        private readonly IOutboxQueueService _outboxQueue;
        private readonly IEncryptionService _encryption;
        private readonly IProfileService _contactService;
        const int InstantSendPayloadThresholdSize = 1024;
        const int InstantSendRecipientCountThreshold = 9;

        private const string RecipientTransitPublicKeyCache = "rtpkc";

        public TransitService(DotYouContext context, ILogger logger, IOutboxQueueService outboxQueue, IStorageService storage, IEncryptionService encryptionSvc, IProfileService contactService, ITransitAuditWriterService auditWriter,
            IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac) : base(context, logger, auditWriter, notificationHub, fac)
        {
            _outboxQueue = outboxQueue;
            _storage = storage;
            _encryption = encryptionSvc;
            _contactService = contactService;
        }

        public async Task<TransferResult> SendBatchNow(IEnumerable<TransferQueueItem> queuedItems)
        {
            var envelopes = queuedItems.Select(i => new Envelope()
            {
                Recipient = i.Recipient,
                FileId = i.FileId
            });

            return await SendBatchNow(envelopes);
        }

        public async Task<TransferResult> SendBatchNow(RecipientList recipients, Guid fileId)
        {
            var envelopes = recipients.Recipients.Select(r => new Envelope()
            {
                Recipient = r,
                FileId = fileId
            });

            return await SendBatchNow(envelopes);
        }

        public async Task<TransferResult> Send(Parcel parcel)
        {
            var fileId = parcel.FileId;
            var recipients = parcel.RecipientList;

            if (fileId == Guid.Empty)
            {
                throw new Exception("Invalid transfer, no file specified");
            }

            _storage.AssertFileIsValid(fileId);

            //if payload size is small, try sending now.
            if (await _storage.GetFileSize(fileId) <= InstantSendPayloadThresholdSize || recipients.Recipients.Length < InstantSendRecipientCountThreshold)
            {
                Console.WriteLine("Length is small, sending now");
                return await this.SendBatchNow(recipients, fileId);
            }

            var result = new TransferResult(Guid.NewGuid());
            Console.WriteLine("Data file is large, putting in queue");
            foreach (var recipient in recipients.Recipients)
            {
                _outboxQueue.Enqueue(new TransferQueueItem()
                {
                    Recipient = recipient,
                    FileId = fileId
                });

                result.QueuedRecipients.Add(recipient);
            }

            return result;
        }

        public void Accept(Guid trackerId, Guid fileId)
        {
            this.AuditWriter.WriteEvent(trackerId, TransitAuditEvent.Accepted);
            throw new NotImplementedException();
        }

        private async Task<TransferResult> SendBatchNow(IEnumerable<Envelope> envelopes)
        {
            var result = new TransferResult(Guid.NewGuid());
            var tasks = new List<Task<SendResult>>();

            foreach (var envelope in envelopes)
            {
                tasks.Add(SendAsync(envelope.Recipient, envelope.FileId));
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
                        FileId = sendResult.FileId
                    };

                    _outboxQueue.EnqueueFailure(item, sendResult.FailureReason.GetValueOrDefault());

                    result.QueuedRecipients.Add(sendResult.Recipient);
                }
            });

            return result;
        }

        private async Task<SendResult> SendAsync(string recipient, Guid fileId)
        {
            TransferFailureReason tfr = TransferFailureReason.UnknownError;
            bool success = false;
            try
            {
                var originalHeader = await _storage.GetKeyHeader(fileId);
                var recipientPublicKey = await GetRecipientTransitPublicKey((DotYouIdentity) recipient);

                if (null == recipientPublicKey)
                {
                    tfr = TransferFailureReason.TransitPublicKeyInvalid;
                }

                var recipientHeader = await _encryption.Encrypt(originalHeader, recipientPublicKey);
                var metaDataStream = new StreamPart(await _storage.GetFilePartStream(fileId, FilePart.Metadata), "metadata.encrypted", "application/json", "metadata");
                var payload = new StreamPart(await _storage.GetFilePartStream(fileId, FilePart.Metadata), "payload.encrypted", "application/x-binary", "payload");

                //TODO: add additional error checking for files existing and successfully being opened, etc.

                var client = base.CreatePerimeterHttpClient<ITransitHostToHostHttpClient>((DotYouIdentity) recipient);
                var result = client.SendHostToHost(recipientHeader, metaDataStream, payload).ConfigureAwait(false).GetAwaiter().GetResult();
                success = result.IsSuccessStatusCode;

                //TODO: add more resolution to these errors (i.e. checking for invalid recipient public key, etc.)
                if (!success)
                {
                    tfr = TransferFailureReason.RecipientServerError;
                }
            }
            catch (EncryptionException)
            {
                tfr = TransferFailureReason.CouldNotEncrypt;
                //TODO: logging
            }
            catch (Exception)
            {
                tfr = TransferFailureReason.UnknownError;
                //TODO: logging
            }

            return new SendResult()
            {
                Recipient = recipient,
                Success = success,
                FailureReason = tfr,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                FileId = fileId
            };
        }

        private async Task<TransitPublicKey> GetRecipientTransitPublicKey(DotYouIdentity recipient)
        {
            //TODO: optimize by reading a dictionary cache
            var tpk = await WithTenantSystemStorageReturnSingle<TransitPublicKey>(RecipientTransitPublicKeyCache, s => s.Get(recipient));

            if (tpk == null || !tpk.IsValid())
            {
                var svc = base.CreatePerimeterHttpClient<ITransitHostToHostHttpClient>(recipient);
                var tpkResponse = await svc.GetTransitPublicKey();

                if (tpkResponse.Content != null && (!tpkResponse.IsSuccessStatusCode || !tpkResponse.Content.IsValid()))
                {
                    this.Logger.LogWarning("Transit public key is invalid");
                    return null;
                }
                
                tpk = tpkResponse.Content;
            }

            return tpk;
        }
    }
}