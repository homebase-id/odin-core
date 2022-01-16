﻿using Refit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Audit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Core.Services.Transit
{
    public class TransitService : TransitServiceBase<ITransitService>, ITransitService
    {
        private readonly IDriveService _driveService;
        private readonly IOutboxService _outboxService;
        private readonly ITransitBoxService _transitBoxService;
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
            IDriveService driveService,
            ITransferKeyEncryptionQueueService transferKeyEncryptionQueueService,
            ITransitAuditWriterService auditWriter,
            ITransitBoxService transitBoxService,
            ISystemStorage systemStorage,
            IDotYouHttpClientFactory dotYouHttpClientFactory) : base(auditWriter)
        {
            _context = context;
            _outboxService = outboxService;
            _driveService = driveService;
            _transferKeyEncryptionQueueService = transferKeyEncryptionQueueService;
            _transitBoxService = transitBoxService;
            _systemStorage = systemStorage;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _logger = logger;
        }


        public async Task<UploadResult> AcceptUpload(UploadPackage package)
        {
            if (package.InstructionSet.TransitOptions?.Recipients?.Contains(_context.HostDotYouId) ?? false)
            {
                throw new UploadException("Cannot transfer a file to the sender; what's the point?");
            }

            //hacky sending the extension for the payload file.  need a proper convention
            var (keyHeader, metadata) = await UnpackMetadata(package);
            await _driveService.StoreLongTerm(keyHeader, metadata, MultipartUploadParts.Payload.ToString());

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

        public async Task AcceptTransfer(DriveFileId file, uint publicKeyCrc)
        {
            _logger.LogInformation($"TransitService.Accept temp fileId:{file.FileId} driveId:{file.DriveId}");

            var item = new TransferBoxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                Sender = this._context.Caller.DotYouId,
                AppId = this._context.TransitContext.AppId, //Note: best to use the appId in from transit context since it's been verified
                TempFile = file,
                PublicKeyCrc = publicKeyCrc,
                Priority = 0 //TODO
            };

            //Note: the inbox service will send the notification
            await _transitBoxService.Add(item);
        }

        private async Task<(KeyHeader keyHeader, FileMetadata metadata)> UnpackMetadata(UploadPackage package)
        {
            var metadataStream = await _driveService.GetTempStream(package.File, MultipartUploadParts.Metadata.ToString());

            byte[] encryptedBytes;
            await using (var ms = new MemoryStream())
            {
                await metadataStream.CopyToAsync(ms);
                encryptedBytes = ms.ToArray();
            }

            var json = AesCbc.DecryptStringFromBytes_Aes(encryptedBytes, this._context.AppContext.GetClientSharedSecret().GetKey(), package.InstructionSet.TransferIv);
            var uploadDescriptor = JsonConvert.DeserializeObject<UploadFileDescriptor>(json);
            var transferEncryptedKeyHeader = uploadDescriptor!.EncryptedKeyHeader;

            if (null == transferEncryptedKeyHeader)
            {
                throw new UploadException("Invalid transfer key header");
            }

            var sharedSecret = _context.AppContext.GetClientSharedSecret().GetKey();
            var keyHeader = transferEncryptedKeyHeader.DecryptAesToKeyHeader(sharedSecret);

            var metadata = new FileMetadata(package.File)
            {
                ContentType = uploadDescriptor.FileMetadata.ContentType,

                AppData = new AppFileMetaData()
                {
                    CategoryId = uploadDescriptor.FileMetadata.AppData.CategoryId,
                    JsonContent = uploadDescriptor.FileMetadata.AppData.JsonContent,
                    ContentIsComplete = uploadDescriptor.FileMetadata.AppData.ContentIsComplete
                }
            };

            return (keyHeader, metadata);
        }

        private async Task<Dictionary<string, TransferStatus>> PrepareTransfer(UploadPackage package)
        {
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
                Recipient = (DotYouIdentity) r,
                AppId = this._context.AppContext.AppId,
                AppClientId = this._context.AppContext.AppClientId
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
                var recipient = (DotYouIdentity) r;
                try
                {
                    //TODO: decide if we should lookup the public key from the recipients host if not cached or just drop the item in the queue
                    var recipientPublicKey = await this.GetRecipientTransitPublicKey(recipient, lookupIfInvalid: true);
                    if (null == recipientPublicKey)
                    {
                        AddToTransferKeyEncryptionQueue(recipient, package);
                        results.Add(recipient, TransferStatus.AwaitingTransferKey);
                        continue;
                    }

                    var header = this.CreateEncryptedRecipientTransferKeyHeader(recipientPublicKey.PublicKey, keyHeader);

                    var item = new RecipientTransferKeyHeaderItem()
                    {
                        Recipient = recipient,
                        Header = header,
                        File = package.File
                    };

                    _systemStorage.WithTenantSystemStorage<RecipientTransferKeyHeaderItem>(RecipientEncryptedTransferKeyHeaderCache, s => s.Save(item));
                    results.Add(recipient, TransferStatus.TransferKeyCreated);
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

        private RsaEncryptedRecipientTransferKeyHeader CreateEncryptedRecipientTransferKeyHeader(byte[] recipientPublicKeyDer, KeyHeader keyHeader)
        {
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(recipientPublicKeyDer);
            var secureKeyHeader = keyHeader.Combine();
            var data = publicKey.Encrypt(secureKeyHeader.GetKey());
            secureKeyHeader.Wipe();

            return new RsaEncryptedRecipientTransferKeyHeader()
            {
                PublicKeyCrc = publicKey.crc32c,
                EncryptedAesKey = data
            };
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
                var transferKeyHeader = await this.GetTransferKeyFromCache(recipient, file);
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

                var transferKeyHeaderBytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(transferKeyHeader));
                var transferKeyHeaderStream = new StreamPart(new MemoryStream(transferKeyHeaderBytes), "transferKeyHeader.encrypted", "application/json", Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));
                var metaDataStream = new StreamPart(await _driveService.GetFilePartStream(file, FilePart.Metadata), "metadata.encrypted", "application/json", Enum.GetName(MultipartHostTransferParts.Metadata));
                var payload = new StreamPart(await _driveService.GetFilePartStream(file, FilePart.Payload), "payload.encrypted", "application/x-binary", Enum.GetName(MultipartHostTransferParts.Payload));

                //TODO: add additional error checking for files existing and successfully being opened, etc.

                var client = _dotYouHttpClientFactory.CreateClient<ITransitHostHttpClient>(recipient, outboxItem.AppId);
                var result = client.SendHostToHost(transferKeyHeaderStream, metaDataStream, payload).ConfigureAwait(false).GetAwaiter().GetResult();
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

        private async Task<RsaEncryptedRecipientTransferKeyHeader> GetTransferKeyFromCache(string recipient, DriveFileId file)
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