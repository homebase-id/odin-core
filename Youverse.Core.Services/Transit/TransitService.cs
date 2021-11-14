using Refit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
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
        private readonly IOutboxQueueService _outboxQueueService;
        private readonly IEncryptionService _encryption;
        private readonly ITransitKeyEncryptionQueueService _transitKeyEncryptionQueueService;

        private const string RecipientEncryptedTransferKeyHeaderCache = "retkhc";
        private const string RecipientTransitPublicKeyCache = "rtpkc";

        public TransitService(DotYouContext context, ILogger logger, IOutboxQueueService outboxQueueService, IStorageService storage, IEncryptionService encryptionSvc, ITransitKeyEncryptionQueueService transitKeyEncryptionQueueService, ITransitAuditWriterService auditWriter,
            IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac) : base(context, logger, auditWriter, notificationHub, fac)
        {
            _outboxQueueService = outboxQueueService;
            _storage = storage;
            _encryption = encryptionSvc;
            _transitKeyEncryptionQueueService = transitKeyEncryptionQueueService;
        }

        public async Task<TransferResult> Send(UploadPackage package)
        {
            _storage.AssertFileIsValid(package.FileId, StorageType.Unknown);

            var storageType = await _storage.GetStorageType(package.FileId);
            if (storageType == StorageType.Temporary)
            {
                await _storage.MoveToLongTerm(package.FileId);
            }

            //a transfer per recipient is added to the outbox queue since there is a background process
            //that will pick up the items and attempt to send.
            _outboxQueueService.Enqueue(package.RecipientList.Recipients.Select(r => new OutboxQueueItem()
            {
                FileId = package.FileId,
                Recipient = r,
            }));


            //Since the owner is online (in this request) we can prepare a transfer key.  the outbox processor
            //will read the transfer key during the background send process
            var keyStatus = await this.PrepareTransferKeys(package);

            var result = new TransferResult()
            {
                FileId = package.FileId,
                RecipientStatus = keyStatus
            };

            return result;
        }

        public void Accept(Guid trackerId, Guid fileId)
        {
            this.AuditWriter.WriteEvent(trackerId, TransitAuditEvent.Accepted);
            throw new NotImplementedException();
        }

        private async Task<Dictionary<DotYouIdentity, TransferStatus>> PrepareTransferKeys(UploadPackage package)
        {
            var results = new Dictionary<DotYouIdentity, TransferStatus>();
            var encryptedKeyHeader = await _storage.GetKeyHeader(package.FileId);

            foreach (var recipient in package.RecipientList.Recipients)
            {
                try
                {
                    //TODO: decide if we should lookup if not cached or just drop in the 
                    var recipientPublicKey = await this.GetRecipientTransitPublicKey(recipient, lookupIfNotCached: true);
                    if (null == recipientPublicKey)
                    {
                        AddToTransitKeyEncryptionQueue(recipient, package);
                        results.Add(recipient, TransferStatus.AwaitingTransferKey);
                    }

                    var header = this.CreateEncryptedRecipientTransferKeyHeader(recipientPublicKey, encryptedKeyHeader);

                    var item = new RecipientTransferKeyHeaderItem()
                    {
                        Recipient = recipient,
                        Header = header,
                        FileId = package.FileId
                    };

                    results.Add(recipient, TransferStatus.TransferKeyCreated);
                    this.WithTenantSystemStorage<RecipientTransferKeyHeaderItem>(RecipientEncryptedTransferKeyHeaderCache, s => s.Save(item));
                }
                catch (Exception e)
                {
                    AddToTransitKeyEncryptionQueue(recipient, package);
                    results.Add(recipient, TransferStatus.AwaitingTransferKey);
                }
            }

            return results;
        }

        private EncryptedRecipientTransferKeyHeader CreateEncryptedRecipientTransferKeyHeader(TransitPublicKey recipientPublicKey, EncryptedKeyHeader encryptedKeyHeader)
        {
            /*
             :Decrypt the __EncryptedKeyHeader__ to get the __KeyHeader__ using the __AppEncryptionKey__;
             :Re-encrypt a copy of the __KeyHeader__ using the __RecipientTransitPublicKey__
                 Result is __EncryptedRecipientTransferKeyHeader__;
             :Store __RecipientTransferKeyHeaderItem__ in  __RecipientTransferKeyHeaderCache__;
            */
            var appEncryptionKey = this.Context.AppContext.GetAppEncryptionKey();

            var encryptedBytes = new byte[] {1, 1, 2, 3, 5, 8, 13, 21};
            var encryptedTransferKey = new EncryptedRecipientTransferKeyHeader()
            {
                EncryptionVersion = 1,
                Data = encryptedBytes
            };

            return encryptedTransferKey;
        }

        private void AddToTransitKeyEncryptionQueue(DotYouIdentity recipient, UploadPackage package)
        {
            var now = DateTimeExtensions.UnixTimeMilliseconds();
            var item = new TransitKeyEncryptionQueueItem()
            {
                FileId = package.FileId,
                AppId = Context.AppContext.AppId,
                Recipient = recipient,
                FirstAddedTimestampMs = now,
                Attempts = 1,
                LastAttemptTimestampMs = now
            };
            _transitKeyEncryptionQueueService.Enqueue(item);
        }

        private async Task<TransferResult> SendBatchNow(IEnumerable<Envelope> envelopes)
        {
            var result = new TransferResult();
            var tasks = new List<Task<SendResult>>();

            foreach (var envelope in envelopes)
            {
                tasks.Add(SendAsync(envelope.Recipient, envelope.FileId));
            }

            await Task.WhenAll(tasks);

            //build results
            tasks.ForEach(task =>
            {
                // var sendResult = task.Result;
                // if (sendResult.Success)
                // {
                //     result.SuccessfulRecipients.Add(sendResult.Recipient);
                // }
                // else
                // {
                //     var item = new OutboxQueueItem()
                //     {
                //         Recipient = sendResult.Recipient,
                //         FileId = sendResult.FileId
                //     };
                //
                //     _outboxQueueService.EnqueueFailure(item, sendResult.FailureReason.GetValueOrDefault());
                //
                //     result.QueuedRecipients.Add(sendResult.Recipient);
                // }
            });

            return result;
        }

        private async Task<SendResult> SendAsync(string recipient, Guid fileId)
        {
            TransferFailureReason tfr = TransferFailureReason.UnknownError;
            bool success = false;
            try
            {
                //look up transfer key

                EncryptedRecipientTransferKeyHeader transferKeyHeader = await this.GetTransferKeyFromCache(recipient, fileId);
                if (null == transferKeyHeader)
                {
                    return new SendResult()
                    {
                        Timestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                        Recipient = recipient,
                        Success = false,
                        FailureReason = TransferFailureReason.EncryptedTransferKeyNotAvailable,
                        FileId = fileId
                    };
                }


                var metaDataStream = new StreamPart(await _storage.GetFilePartStream(fileId, FilePart.Metadata), "metadata.encrypted", "application/json", Enum.GetName(FilePart.Metadata));
                var payload = new StreamPart(await _storage.GetFilePartStream(fileId, FilePart.Metadata), "payload.encrypted", "application/x-binary", Enum.GetName(FilePart.Payload));

                //TODO: add additional error checking for files existing and successfully being opened, etc.

                var client = base.CreatePerimeterHttpClient<ITransitHostToHostHttpClient>((DotYouIdentity) recipient);
                var result = client.SendHostToHost(transferKeyHeader, metaDataStream, payload).ConfigureAwait(false).GetAwaiter().GetResult();
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
                Timestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                FileId = fileId
            };
        }

        private async Task<EncryptedRecipientTransferKeyHeader> GetTransferKeyFromCache(string recipient, Guid fileId)
        {
            var item = await this.WithTenantSystemStorageReturnSingle<RecipientTransferKeyHeaderItem>(RecipientEncryptedTransferKeyHeaderCache, s => s.FindOne(r => r.Recipient == recipient && r.FileId == fileId));
            return item?.Header;
        }

        private async Task<TransitPublicKey> GetRecipientTransitPublicKey(DotYouIdentity recipient, bool lookupIfNotCached = true)
        {
            //TODO: optimize by reading a dictionary cache
            var tpk = await WithTenantSystemStorageReturnSingle<TransitPublicKey>(RecipientTransitPublicKeyCache, s => s.Get(recipient));


            if ((tpk == null || !tpk.IsValid()) && lookupIfNotCached)
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


    /// <summary>
    /// The encrypted version of the KeyHeader for a given recipient
    /// which as been encrypted using the RecipientTransitPublicKey
    /// </summary>
    public class EncryptedRecipientTransferKeyHeader
    {
        public int EncryptionVersion { get; set; }
        public byte[] Data { get; set; }
    }

    public class RecipientTransferKeyHeaderItem
    {
        public Guid FileId { get; set; }
        public DotYouIdentity Recipient { get; set; }
        public EncryptedRecipientTransferKeyHeader Header { get; set; }
    }
}