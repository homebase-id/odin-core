using Refit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Audit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Inbox;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Core.Services.Transit
{
    public class TransitService : TransitServiceBase<ITransitService>, ITransitService
    {
        private readonly IStorageService _storage;
        private readonly IOutboxService _outboxService;
        private readonly IInboxService _inboxService;
        private readonly IEncryptionService _encryption;
        private readonly ITransferKeyEncryptionQueueService _transferKeyEncryptionQueueService;
        private readonly DotYouContext _context;
        private readonly ILogger<TransitService> _logger;
        private readonly ISystemStorage _systemStorage;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        
        private const string RecipientEncryptedTransferKeyHeaderCache = "retkhc";
        private const string RecipientTransitPublicKeyCache = "rtpkc";

        public TransitService(DotYouContext context,
            ILogger<TransitService> logger,
            IOutboxService outboxService,
            IStorageService storage,
            IEncryptionService encryptionSvc,
            ITransferKeyEncryptionQueueService transferKeyEncryptionQueueService,
            ITransitAuditWriterService auditWriter,
            IInboxService inboxService, 
            ISystemStorage systemStorage, 
            IDotYouHttpClientFactory dotYouHttpClientFactory) : base(auditWriter)
        {
            _context = context;
            _outboxService = outboxService;
            _storage = storage;
            _encryption = encryptionSvc;
            _transferKeyEncryptionQueueService = transferKeyEncryptionQueueService;
            _inboxService = inboxService;
            _systemStorage = systemStorage;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _logger = logger;
        }

        public async Task<TransferResult> PrepareTransfer(UploadPackage package)
        {
            _storage.AssertFileIsValid(package.FileId, StorageDisposition.Unknown);

            var storageType = await _storage.GetStorageType(package.FileId);
            if (storageType == StorageDisposition.Temporary)
            {
                await _storage.MoveToLongTerm(package.FileId);
            }

            //TODO: consider if the recipient transfer key header should go directly in the outbox


            //Since the owner is online (in this request) we can prepare a transfer key.  the outbox processor
            //will read the transfer key during the background send process
            var keyStatus = await this.PrepareTransferKeys(package);

            //a transfer per recipient is added to the outbox queue since there is a background process
            //that will pick up the items and attempt to send.
            await _outboxService.Add(package.RecipientList.Recipients.Select(r => new OutboxItem()
            {
                FileId = package.FileId,
                Recipient = r,
                AppId = this._context.AppContext.AppId,
                DeviceUid = this._context.AppContext.DeviceUid
            }));

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

            _logger.LogInformation($"TransitService.Accept fileId:{fileId}");

            //TODO: app routing, app notification and so on
            //Get the app Inbox storage
            // store item 
            var item = new InboxItem()
            {
                Id = Guid.NewGuid(),
                Sender = this._context.Caller.DotYouId,
                AppId = this._context.AppContext.AppId,
                FileId = fileId, 
                TrackerId = trackerId
            };

            _inboxService.Add(item);
        }

        private async Task<Dictionary<string, TransferStatus>> PrepareTransferKeys(UploadPackage package)
        {
            var results = new Dictionary<string, TransferStatus>();
            var encryptedKeyHeader = await _storage.GetKeyHeader(package.FileId);

            foreach (var recipient in package.RecipientList.Recipients)
            {
                try
                {
                    //TODO: decide if we should lookup the public key from the recipients host if not cached or just drop the item in the queue
                    var recipientPublicKey = await this.GetRecipientTransitPublicKey(recipient, lookupIfInvalid: true);
                    if (null == recipientPublicKey)
                    {
                        AddToTransferKeyEncryptionQueue(recipient, package);
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
                    _systemStorage.WithTenantSystemStorage<RecipientTransferKeyHeaderItem>(RecipientEncryptedTransferKeyHeaderCache, s => s.Save(item));
                }
                catch (Exception)
                {
                    AddToTransferKeyEncryptionQueue(recipient, package);
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
            var appEncryptionKey = this._context.AppContext.GetAppEncryptionKey();

            var encryptedBytes = new byte[] { 1, 1, 2, 3, 5, 8, 13, 21 };
            var encryptedTransferKey = new EncryptedRecipientTransferKeyHeader()
            {
                EncryptionVersion = 1,
                Data = encryptedBytes
            };

            return encryptedTransferKey;
        }

        private void AddToTransferKeyEncryptionQueue(DotYouIdentity recipient, UploadPackage package)
        {
            var now = DateTimeExtensions.UnixTimeMilliseconds();
            var item = new TransitKeyEncryptionQueueItem()
            {
                FileId = package.FileId,
                AppId = _context.AppContext.AppId,
                Recipient = recipient,
                FirstAddedTimestampMs = now,
                Attempts = 1,
                LastAttemptTimestampMs = now
            };

            _transferKeyEncryptionQueueService.Enqueue(item);
        }

        public async Task SendBatchNow(IEnumerable<OutboxItem> items)
        {
            var tasks = new List<Task<SendResult>>();

            foreach (var item in items)
            {
                tasks.Add(SendAsync(item));
            }

            await Task.WhenAll(tasks);

            //build results
            tasks.ForEach(task =>
            {
                var sendResult = task.Result;
                if (sendResult.Success)
                {
                    _outboxService.Remove(sendResult.Recipient, sendResult.FileId);
                }
                else
                {
                    _outboxService.MarkFailure(sendResult.OutboxItemId, sendResult.FailureReason.GetValueOrDefault());
                }
            });

        }

        private async Task<SendResult> SendAsync(OutboxItem outboxItem)
        {
            DotYouIdentity recipient = outboxItem.Recipient;
            Guid fileId = outboxItem.FileId;

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
                        OutboxItemId = outboxItem.Id,
                        FileId = fileId,
                        Recipient = recipient,
                        Timestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                        Success = false,
                        FailureReason = TransferFailureReason.EncryptedTransferKeyNotAvailable
                    };
                }

                var metaDataStream = new StreamPart(await _storage.GetFilePartStream(fileId, FilePart.Metadata), "metadata.encrypted", "application/json", Enum.GetName(FilePart.Metadata));
                var payload = new StreamPart(await _storage.GetFilePartStream(fileId, FilePart.Payload), "payload.encrypted", "application/x-binary", Enum.GetName(FilePart.Payload));

                //TODO: add additional error checking for files existing and successfully being opened, etc.

                //HACK: i override the appId here so the recipient server knows the corresponding
                //app.  I'd rather load this into app context some how
                var client = _dotYouHttpClientFactory.CreateClient<ITransitHostHttpClient>(recipient, outboxItem.AppId);
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
                FileId = fileId,
                Recipient = recipient,
                OutboxItemId = outboxItem.Id,
                Success = success,
                FailureReason = tfr,
                Timestamp = DateTimeExtensions.UnixTimeMilliseconds()
            };
        }

        private async Task<EncryptedRecipientTransferKeyHeader> GetTransferKeyFromCache(string recipient, Guid fileId)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<RecipientTransferKeyHeaderItem>(RecipientEncryptedTransferKeyHeaderCache, s => s.FindOne(r => r.Recipient == recipient && r.FileId == fileId));
            return item?.Header;
        }

        private async Task<TransitPublicKey> GetRecipientTransitPublicKey(DotYouIdentity recipient, bool lookupIfInvalid = true)
        {
            //HACK: waiting on bouncy castle
            return new TransitPublicKey()
            {
                Crc = 0,
                Expiration = (UInt64)DateTimeOffset.UtcNow.AddDays(100).ToUnixTimeMilliseconds(),
                PublicKey = Guid.Empty.ToByteArray()
            };

            //TODO: optimize by reading a dictionary cache
            var tpk = await _systemStorage.WithTenantSystemStorageReturnSingle<TransitPublicKey>(RecipientTransitPublicKeyCache, s => s.Get(recipient));

            if ((tpk == null || !tpk.IsValid()) && lookupIfInvalid)
            {
                var svc = _dotYouHttpClientFactory.CreateClient<ITransitHostHttpClient>(recipient);
                var tpkResponse = await svc.GetTransitPublicKey();

                if (tpkResponse.Content != null && (!tpkResponse.IsSuccessStatusCode || !tpkResponse.Content.IsValid()))
                {
                    this._logger.LogWarning("Transit public key is invalid");
                    return null;
                }

                tpk = tpkResponse.Content;
                _systemStorage.WithTenantSystemStorage<TransitPublicKey>(RecipientTransitPublicKeyCache, s => s.Save(tpk));
            }

            return tpk;
        }
    }
}