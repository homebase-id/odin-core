﻿using Refit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Core.Services.Transit
{
    public class TransitService : TransitServiceBase<ITransitService>, ITransitService
    {
        private readonly IDriveService _driveService;
        private readonly IOutboxService _outboxService;
        private readonly ITransitBoxService _transitBoxService;
        private readonly ITransferKeyEncryptionQueueService _transferKeyEncryptionQueueService;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ILogger<TransitService> _logger;
        private readonly ISystemStorage _systemStorage;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly TenantContext _tenantContext;
        private readonly ICircleNetworkService _circleNetworkService;
        private readonly IPublicKeyService _publicKeyService;
        private const string RecipientEncryptedTransferKeyHeaderCache = "retkhc";
        private const string RecipientTransitPublicKeyCache = "rtpkc";

        public TransitService(DotYouContextAccessor contextAccessor,
            ILogger<TransitService> logger,
            IOutboxService outboxService,
            IDriveService driveService,
            ITransferKeyEncryptionQueueService transferKeyEncryptionQueueService,
            ITransitBoxService transitBoxService,
            ISystemStorage systemStorage,
            IDotYouHttpClientFactory dotYouHttpClientFactory, TenantContext tenantContext, ICircleNetworkService circleNetworkService, IPublicKeyService publicKeyService) : base()
        {
            _contextAccessor = contextAccessor;
            _outboxService = outboxService;
            _driveService = driveService;
            _transferKeyEncryptionQueueService = transferKeyEncryptionQueueService;
            _transitBoxService = transitBoxService;
            _systemStorage = systemStorage;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _tenantContext = tenantContext;
            _circleNetworkService = circleNetworkService;
            _publicKeyService = publicKeyService;
            _logger = logger;
        }


        public async Task<UploadResult> AcceptUpload(UploadPackage package)
        {
            if (package.InstructionSet.TransitOptions?.Recipients?.Contains(_tenantContext.HostDotYouId) ?? false)
            {
                throw new UploadException("Cannot transfer a file to the sender; what's the point?");
            }

            //hacky sending the extension for the payload file.  need a proper convention
            var (keyHeader, metadata, serverMetadata) = await UnpackMetadata(package);

            if (null == serverMetadata.AccessControlList)
            {
                throw new MissingDataException("Access control list must be specified");
            }

            await _driveService.CommitTempFileToLongTerm(package.InternalFile, keyHeader, metadata, serverMetadata, MultipartUploadParts.Payload.ToString());

            var ext = new ExternalFileIdentifier()
            {
                TargetDrive = _driveService.GetDrive(package.InternalFile.DriveId).Result.GetTargetDrive(),
                FileId = package.InternalFile.FileId
            };

            var tx = new UploadResult()
            {
                File = ext
            };

            var recipients = package.InstructionSet.TransitOptions?.Recipients ?? null;
            if (null != recipients)
            {
                tx.RecipientStatus = await PrepareTransfer(package);
            }

            return tx;
        }

        public async Task AcceptTransfer(InternalDriveFileId file, uint publicKeyCrc)
        {
            _logger.LogInformation($"TransitService.AcceptTransfer temp fileId:{file.FileId} driveId:{file.DriveId}");

            var item = new TransferBoxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                Sender = this._contextAccessor.GetCurrent().Caller.DotYouId,
                AppId = this._contextAccessor.GetCurrent().AppContext.AppId,
                TempFile = file,
                PublicKeyCrc = publicKeyCrc,
                Priority = 0 //TODO
            };

            //Note: the inbox service will send the notification
            await _transitBoxService.Add(item);
        }

        private async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadata(UploadPackage package)
        {
            var metadataStream = await _driveService.GetTempStream(package.InternalFile, MultipartUploadParts.Metadata.ToString());

            var clientSharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
            var jsonBytes = AesCbc.Decrypt(metadataStream.ToByteArray(), ref clientSharedSecret, package.InstructionSet.TransferIv);
            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            var uploadDescriptor = JsonConvert.DeserializeObject<UploadFileDescriptor>(json);
            var transferEncryptedKeyHeader = uploadDescriptor!.EncryptedKeyHeader;

            if (null == transferEncryptedKeyHeader)
            {
                throw new UploadException("Invalid transfer key header");
            }

            KeyHeader keyHeader = uploadDescriptor.FileMetadata.PayloadIsEncrypted ? transferEncryptedKeyHeader.DecryptAesToKeyHeader(ref clientSharedSecret) : KeyHeader.Empty();
            var metadata = new FileMetadata(package.InternalFile)
            {
                ContentType = uploadDescriptor.FileMetadata.ContentType,

                //TODO: need an automapper *sigh
                AppData = new AppFileMetaData()
                {
                    Tags = uploadDescriptor.FileMetadata.AppData.Tags,

                    FileType = uploadDescriptor.FileMetadata.AppData.FileType,
                    DataType = uploadDescriptor.FileMetadata.AppData.DataType,
                    UserDate = uploadDescriptor.FileMetadata.AppData.UserDate,
                    
                    JsonContent = uploadDescriptor.FileMetadata.AppData.JsonContent,
                    ContentIsComplete = uploadDescriptor.FileMetadata.AppData.ContentIsComplete
                },

                PayloadIsEncrypted = uploadDescriptor.FileMetadata.PayloadIsEncrypted,
                SenderDotYouId = uploadDescriptor.FileMetadata.SenderDotYouId
            };

            var serverMetadata = new ServerMetadata()
            {
                AccessControlList = uploadDescriptor.FileMetadata.AccessControlList
            };

            return (keyHeader, metadata, serverMetadata);
        }

        private async Task<Dictionary<string, TransferStatus>> PrepareTransfer(UploadPackage package)
        {
            //TODO: consider if the recipient transfer key header should go directly in the outbox

            //Since the owner is online (in this request) we can prepare a transfer key.  the outbox processor
            //will read the transfer key during the background send process
            var keyStatus = await this.PrepareTransferInstructionSet(package);

            //a transfer per recipient is added to the outbox queue since there is a background process
            //that will pick up the items and attempt to send.
            var recipients = package.InstructionSet.TransitOptions?.Recipients ?? new List<string>();
            await _outboxService.Add(recipients.Select(r => new OutboxItem()
            {
                File = package.InternalFile,
                Recipient = (DotYouIdentity)r,
                AppId = this._contextAccessor.GetCurrent().AppContext.AppId,
                AccessRegistrationId = this._contextAccessor.GetCurrent().PermissionsContext.AccessRegistrationId
            }));

            return keyStatus;
        }

        private async Task<Dictionary<string, TransferStatus>> PrepareTransferInstructionSet(UploadPackage package)
        {
            var results = new Dictionary<string, TransferStatus>();
            var header = await _driveService.GetServerFileHeader(package.InternalFile);

            var storageKey = this._contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(package.InternalFile.DriveId);
            var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
            storageKey.Wipe();

            foreach (var r in package.InstructionSet.TransitOptions?.Recipients ?? new List<string>())
            {
                var recipient = (DotYouIdentity)r;
                try
                {
                    //TODO: decide if we should lookup the public key from the recipients host if not cached or just drop the item in the queue
                    var pk = await _publicKeyService.GetRecipientOfflinePublicKey(recipient, true, false);
                    if (null == pk)
                    {
                        AddToTransferKeyEncryptionQueue(recipient, package);
                        results.Add(recipient, TransferStatus.AwaitingTransferKey);
                        continue;
                    }

                    //TODO: examine how we can avoid using the override hack on GetIdentityConnectionRegistration
                    var clientAuthToken = _circleNetworkService.GetConnectionAuthToken(recipient, true, true).GetAwaiter().GetResult();
                    var instructionSet = this.CreateEncryptedRecipientTransferInstructionSet(pk.publicKey, keyHeader, clientAuthToken, package.InstructionSet.StorageOptions.Drive);

                    var item = new RecipientTransferInstructionSetItem()
                    {
                        Recipient = recipient,
                        InstructionSet = instructionSet,
                        File = package.InternalFile
                    };

                    _systemStorage.WithTenantSystemStorage<RecipientTransferInstructionSetItem>(RecipientEncryptedTransferKeyHeaderCache, s => s.Save(item));
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

        private RsaEncryptedRecipientTransferInstructionSet CreateEncryptedRecipientTransferInstructionSet(byte[] recipientPublicKeyDer, KeyHeader keyHeader,
            ClientAuthenticationToken clientAuthenticationToken, TargetDrive drive)
        {
            //TODO: need to review how to decrypt the private key on the recipient side
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(recipientPublicKeyDer);

            // var secureKeyHeader = keyHeader.Combine();
            // var rsaEncryptedKeyHeader = publicKey.Encrypt(secureKeyHeader.GetKey());
            // secureKeyHeader.Wipe();

            var combinedKey = keyHeader.Combine();
            var rsaEncryptedKeyHeader = publicKey.Encrypt(combinedKey.GetKey());
            combinedKey.Wipe();

            //TODO: need to encrypt the client access token here with something on my server side (therefore, we cannot use RSA encryption)
            var encryptedClientAccessToken = clientAuthenticationToken.ToString().ToUtf8ByteArray();

            return new RsaEncryptedRecipientTransferInstructionSet()
            {
                PublicKeyCrc = publicKey.crc32c,
                EncryptedAesKeyHeader = rsaEncryptedKeyHeader,
                EncryptedClientAuthToken = encryptedClientAccessToken,
                Drive = drive
            };
        }

        private void AddToTransferKeyEncryptionQueue(DotYouIdentity recipient, UploadPackage package)
        {
            var now = DateTimeExtensions.UnixTimeMilliseconds();
            var item = new TransitKeyEncryptionQueueItem()
            {
                FileId = package.InternalFile.FileId,
                AppId = _contextAccessor.GetCurrent().AppContext.AppId,
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
                var transferInstructionSet = await this.GetTransferInstructionSetFromCache(recipient, file);
                if (null == transferInstructionSet)
                {
                    return new SendResult()
                    {
                        OutboxItemId = outboxItem.Id,
                        File = file,
                        Recipient = recipient,
                        Timestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                        Success = false,
                        FailureReason = TransferFailureReason.EncryptedTransferInstructionSetNotAvailable
                    };
                }

                var transferKeyHeaderBytes = JsonConvert.SerializeObject(transferInstructionSet).ToUtf8ByteArray();
                var transferKeyHeaderStream = new StreamPart(new MemoryStream(transferKeyHeaderBytes), "transferKeyHeader.encrypted", "application/json",
                    Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

                //TODO: here I am removing the file and drive id from the stream but we need to resolve this by moving the file information to the server header
                var header = await _driveService.GetServerFileHeader(file);

                var metadata = header.FileMetadata;

                //redact the info by explicitly stating what we will keep
                //therefore, if a new attribute is added, it must be considered 
                //if it should be sent to the recipient
                var redactedMetadata = new FileMetadata()
                {
                    File = InternalDriveFileId.Redacted(),
                    Created = metadata.Created,
                    Updated = metadata.Updated,
                    AppData = metadata.AppData,
                    PayloadIsEncrypted = metadata.PayloadIsEncrypted,
                    ContentType = metadata.ContentType,
                    SenderDotYouId = string.Empty
                };

                var json = JsonConvert.SerializeObject(redactedMetadata);
                var stream = new MemoryStream(json.ToUtf8ByteArray());
                var metaDataStream = new StreamPart(stream, "metadata.encrypted", "application/json", Enum.GetName(MultipartHostTransferParts.Metadata));

                var payload = new StreamPart(await _driveService.GetPayloadStream(file), "payload.encrypted", "application/x-binary", Enum.GetName(MultipartHostTransferParts.Payload));

                //TODO: add additional error checking for files existing and successfully being opened, etc.

                //TODO: here we need to decrypt the token. 
                var decryptedClientAuthTokenBytes = transferInstructionSet.EncryptedClientAuthToken;
                var clientAuthToken = ClientAuthenticationToken.Parse(decryptedClientAuthTokenBytes.ToStringFromUTF8Bytes());
                decryptedClientAuthTokenBytes.WriteZeros();

                var client = _dotYouHttpClientFactory.CreateClientUsingAccessToken<ITransitHostHttpClient>(recipient, clientAuthToken, outboxItem.AppId);
                var response = client.SendHostToHost(transferKeyHeaderStream, metaDataStream, payload).ConfigureAwait(false).GetAwaiter().GetResult();
                success = response.IsSuccessStatusCode;

                // var result = response.Content;
                //
                // switch (result.Code)
                // {
                //     case TransitResponseCode.Accepted:
                //         break;
                //     case TransitResponseCode.QuarantinedPayload:
                //         break;
                //     case TransitResponseCode.QuarantinedSenderNotConnected:
                //         break;
                //     case TransitResponseCode.Rejected:
                //         break;
                //     default:
                //         throw new ArgumentOutOfRangeException();
                // }
                //
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

        private async Task<RsaEncryptedRecipientTransferInstructionSet> GetTransferInstructionSetFromCache(string recipient, InternalDriveFileId file)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<RecipientTransferInstructionSetItem>(RecipientEncryptedTransferKeyHeaderCache,
                s => s.FindOne(r => r.Recipient == recipient && r.File == file));
            return item?.InstructionSet;
        }
    }
}