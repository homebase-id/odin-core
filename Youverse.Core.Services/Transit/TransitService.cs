﻿using Refit;
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
        private readonly IDriveService _driveService;
        private readonly IOutboxService _outboxService;
        private readonly IInboxService _inboxService;
        private readonly ITransferKeyEncryptionQueueService _transferKeyEncryptionQueueService;
        private readonly DotYouContext _context;
        private readonly ILogger<TransitService> _logger;
        private readonly ISystemStorage _systemStorage;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly IDriveQueryService _queryService; 
        
        //HACK
        //query service is here just to get it initialized by DI

        private const string RecipientEncryptedTransferKeyHeaderCache = "retkhc";
        private const string RecipientTransitPublicKeyCache = "rtpkc";

        public TransitService(DotYouContext context,
            ILogger<TransitService> logger,
            IOutboxService outboxService,
            IDriveService driveService,
            ITransferKeyEncryptionQueueService transferKeyEncryptionQueueService,
            ITransitAuditWriterService auditWriter,
            IInboxService inboxService,
            ISystemStorage systemStorage,
            IDotYouHttpClientFactory dotYouHttpClientFactory, 
            IDriveQueryService queryService) : base(auditWriter)
        {
            _context = context;
            _outboxService = outboxService;
            _driveService = driveService;
            _transferKeyEncryptionQueueService = transferKeyEncryptionQueueService;
            _inboxService = inboxService;
            _systemStorage = systemStorage;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _queryService = queryService;
            _logger = logger;
        }

        public async Task<UploadResult> AcceptUpload(UploadPackage package)
        {
            if (package.InstructionSet.TransitOptions?.Recipients?.Contains(_context.HostDotYouId) ?? false)
            {
                throw new UploadException("Cannot transfer a file to the sender; what's the point?");
            }
            
            _driveService.AssertFileIsValid(package.File, StorageDisposition.Unknown);

            var storageType = await _driveService.GetStorageType(package.File);
            if (storageType == StorageDisposition.Temporary)
            {
                await _driveService.MoveToLongTerm(package.File);
            }

            var tx = new UploadResult()
            {
                File = package.File
            };

            var recipients = package.InstructionSet.TransitOptions?.Recipients ?? null;
            if (null != recipients)
            {
                tx.RecipientStatus = await PrepareTransfer(package);
            }

            return tx;
        }

        public void AcceptTransfer(Guid trackerId, DriveFileId file)
        {
            this.AuditWriter.WriteEvent(trackerId, TransitAuditEvent.Accepted);

            _logger.LogInformation($"TransitService.Accept fileId:{file.FileId} driveId:{file.DriveId}");

            //TODO: app routing, app notification and so on
            //Get the app Inbox storage
            // store item 
            var item = new InboxItem()
            {
                Id = Guid.NewGuid(),
                Sender = this._context.Caller.DotYouId,
                AppId = this._context.AppContext.AppId,
                File = file,
                TrackerId = trackerId
            };

            _inboxService.Add(item);
        }

        private async Task<Dictionary<string, TransferStatus>> PrepareTransfer(UploadPackage package)
        {
            _driveService.AssertFileIsValid(package.File, StorageDisposition.Unknown);

            var storageType = await _driveService.GetStorageType(package.File);
            if (storageType == StorageDisposition.Temporary)
            {
                await _driveService.MoveToLongTerm(package.File);
            }

            //TODO: consider if the recipient transfer key header should go directly in the outbox
            
            //Since the owner is online (in this request) we can prepare a transfer key.  the outbox processor
            //will read the transfer key during the background send process
            var keyStatus = await this.PrepareTransferKeys(package);

            //a transfer per recipient is added to the outbox queue since there is a background process
            //that will pick up the items and attempt to send.
            var recipients = package.InstructionSet.TransitOptions?.Recipients ?? new List<string>();
            await _outboxService.Add(recipients.Select(r => new OutboxItem()
            {
                File = package.File,
                Recipient = (DotYouIdentity)r,
                AppId = this._context.AppContext.AppId,
                DeviceUid = Convert.ToBase64String(this._context.AppContext.DeviceUid)
            }));

            return keyStatus;
        }

        private async Task<Dictionary<string, TransferStatus>> PrepareTransferKeys(UploadPackage package)
        {
            var results = new Dictionary<string, TransferStatus>();
            var encryptedKeyHeader = await _driveService.GetEncryptedKeyHeader(package.File);
            var storageKey = this._context.AppContext.GetDriveStorageKey(package.File.DriveId);
            var keyHeader = encryptedKeyHeader.DecryptAesToKeyHeader(storageKey.GetKey());
            storageKey.Wipe();

            foreach (var r in package.InstructionSet.TransitOptions?.Recipients ?? new List<string>())
            {
                var recipient = (DotYouIdentity)r;
                try
                {
                    //TODO: decide if we should lookup the public key from the recipients host if not cached or just drop the item in the queue
                    var recipientPublicKey = await this.GetRecipientTransitPublicKey(recipient, lookupIfInvalid: true);
                    if (null == recipientPublicKey)
                    {
                        AddToTransferKeyEncryptionQueue(recipient, package);
                        results.Add(recipient, TransferStatus.AwaitingTransferKey);
                    }
                    
                    var header = this.CreateEncryptedRecipientTransferKeyHeader(recipientPublicKey, keyHeader);
                    // var header = this.CreateEncryptedRecipientTransferKeyHeader(null, keyHeader);

                    var item = new RecipientTransferKeyHeaderItem()
                    {
                        Recipient = recipient,
                        Header = header,
                        File = package.File
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

            //TODO: keyHeader.AesKey.Wipe();

            return results;
        }

        private EncryptedRecipientTransferKeyHeader CreateEncryptedRecipientTransferKeyHeader(TransitPublicKey recipientPublicKey, KeyHeader keyHeader)
        {
            /*
             :Re-encrypt a copy of the __KeyHeader__ using the __RecipientTransitPublicKey__
                 Result is __EncryptedRecipientTransferKeyHeader__;
             :Store __RecipientTransferKeyHeaderItem__ in  __RecipientTransferKeyHeaderCache__;
            */
            //TODO: encrypt header using RSA Public Key
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
                FileId = package.File.FileId,
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
                    _outboxService.Remove(sendResult.OutboxItemId);
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
            var file = outboxItem.File;

            TransferFailureReason tfr = TransferFailureReason.UnknownError;
            bool success = false;
            try
            {
                //look up transfer key
                EncryptedRecipientTransferKeyHeader transferKeyHeader = await this.GetTransferKeyFromCache(recipient, file);
                if (null == transferKeyHeader)
                {
                    return new SendResult()
                    {
                        OutboxItemId = outboxItem.Id,
                        File = file,
                        Recipient = recipient,
                        Timestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                        Success = false,
                        FailureReason = TransferFailureReason.EncryptedTransferKeyNotAvailable
                    };
                }

                var metaDataStream = new StreamPart(await _driveService.GetFilePartStream(file, FilePart.Metadata), "metadata.encrypted", "application/json", Enum.GetName(FilePart.Metadata));
                var payload = new StreamPart(await _driveService.GetFilePartStream(file, FilePart.Payload), "payload.encrypted", "application/x-binary", Enum.GetName(FilePart.Payload));

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
                File = file,
                Recipient = recipient,
                OutboxItemId = outboxItem.Id,
                Success = success,
                FailureReason = tfr,
                Timestamp = DateTimeExtensions.UnixTimeMilliseconds()
            };
        }

        private async Task<EncryptedRecipientTransferKeyHeader> GetTransferKeyFromCache(string recipient, DriveFileId file)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<RecipientTransferKeyHeaderItem>(RecipientEncryptedTransferKeyHeaderCache, s => s.FindOne(r => r.Recipient == recipient && r.File == file));
            return item?.Header;
        }

        private async Task<TransitPublicKey> GetRecipientTransitPublicKey(DotYouIdentity recipient, bool lookupIfInvalid = true)
        {
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