using Refit;
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
using Youverse.Core.Services.Transit.Audit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Contacts.Circle.Membership;
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

        private const string RecipientEncryptedTransferKeyHeaderCache = "retkhc";
        private const string RecipientTransitPublicKeyCache = "rtpkc";

        public TransitService(DotYouContextAccessor contextAccessor,
            ILogger<TransitService> logger,
            IOutboxService outboxService,
            IDriveService driveService,
            ITransferKeyEncryptionQueueService transferKeyEncryptionQueueService,
            ITransitAuditWriterService auditWriter,
            ITransitBoxService transitBoxService,
            ISystemStorage systemStorage,
            IDotYouHttpClientFactory dotYouHttpClientFactory, TenantContext tenantContext, ICircleNetworkService circleNetworkService) : base(auditWriter)
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
            _logger = logger;
        }


        public async Task<UploadResult> AcceptUpload(UploadPackage package)
        {
            if (package.InstructionSet.TransitOptions?.Recipients?.Contains(_tenantContext.HostDotYouId) ?? false)
            {
                throw new UploadException("Cannot transfer a file to the sender; what's the point?");
            }

            //hacky sending the extension for the payload file.  need a proper convention
            var (keyHeader, metadata) = await UnpackMetadata(package);
            await _driveService.StoreLongTerm(package.InternalFile, keyHeader, metadata, MultipartUploadParts.Payload.ToString());

            if (null == metadata.AccessControlList)
            {
                throw new MissingDataException("Access control list must be specified");
            }

            var ext = new ExternalFileIdentifier()
            {
                DriveAlias = _driveService.GetDrive(package.InternalFile.DriveId).Result.Alias,
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
            _logger.LogInformation($"TransitService.Accept temp fileId:{file.FileId} driveId:{file.DriveId}");

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

        private async Task<(KeyHeader keyHeader, FileMetadata metadata)> UnpackMetadata(UploadPackage package)
        {
            var metadataStream = await _driveService.GetTempStream(package.InternalFile, MultipartUploadParts.Metadata.ToString());

            var clientSharedSecret = _contextAccessor.GetCurrent().AppContext.ClientSharedSecret;
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
                    JsonContent = uploadDescriptor.FileMetadata.AppData.JsonContent,
                    ContentIsComplete = uploadDescriptor.FileMetadata.AppData.ContentIsComplete,
                    Alias = uploadDescriptor.FileMetadata.AppData.Alias
                },

                PayloadIsEncrypted = uploadDescriptor.FileMetadata.PayloadIsEncrypted,
                SenderDotYouId = uploadDescriptor.FileMetadata.SenderDotYouId,
                AccessControlList = uploadDescriptor.FileMetadata.AccessControlList
            };

            return (keyHeader, metadata);
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
                Recipient = (DotYouIdentity) r,
                AppId = this._contextAccessor.GetCurrent().AppContext.AppId,
                AccessRegistrationId = this._contextAccessor.GetCurrent().PermissionsContext.AccessRegistrationId
            }));

            return keyStatus;
        }

        private async Task<Dictionary<string, TransferStatus>> PrepareTransferInstructionSet(UploadPackage package)
        {
            var results = new Dictionary<string, TransferStatus>();
            var encryptedKeyHeader = await _driveService.GetEncryptedKeyHeader(package.InternalFile);
            var storageKey = this._contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(package.InternalFile.DriveId);
            var keyHeader = encryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
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

                    //TODO: examine how we can avoid using the override hack on GetIdentityConnectionRegistration
                    var identityReg = _circleNetworkService.GetIdentityConnectionRegistration(recipient, true).GetAwaiter().GetResult();
                    if (!identityReg.IsConnected())
                    {
                        //TODO: throwing an exception here would result in a partial send.  need to return an error code and status instead
                        throw new MissingDataException("Cannot send transfer a recipient to which you're not connected.");
                    }
                    
                    var clientAuthToken = identityReg.CreateClientAuthToken();
                    var instructionSet = this.CreateEncryptedRecipientTransferInstructionSet(recipientPublicKey.PublicKeyData.publicKey, keyHeader, clientAuthToken, package.InstructionSet.StorageOptions.DriveAlias);

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

        private RsaEncryptedRecipientTransferInstructionSet CreateEncryptedRecipientTransferInstructionSet(byte[] recipientPublicKeyDer, KeyHeader keyHeader, ClientAuthenticationToken clientAuthenticationToken, Guid driveAlias)
        {
            //TODO: need to review how to decrypt the private key on the recipient side
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(recipientPublicKeyDer);
            // var secureKeyHeader = keyHeader.Combine();
            // var rsaEncryptedKeyHeader = publicKey.Encrypt(secureKeyHeader.GetKey());
            // secureKeyHeader.Wipe();
            var rsaEncryptedKeyHeader = keyHeader.Combine().GetKey();

            //TODO: need to encrypt the client access token here with something on my server side
            // var rsaEncryptedClientAccessToken = publicKey.Encrypt(clientAuthToken.ToString().ToUtf8ByteArray());
            var encryptedClientAccessToken = clientAuthenticationToken.ToString().ToUtf8ByteArray();

            return new RsaEncryptedRecipientTransferInstructionSet()
            {
                PublicKeyCrc = publicKey.crc32c,
                EncryptedAesKeyHeader = rsaEncryptedKeyHeader,
                EncryptedClientAuthToken = encryptedClientAccessToken,
                DriveAlias = driveAlias
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
                var transferKeyHeaderStream = new StreamPart(new MemoryStream(transferKeyHeaderBytes), "transferKeyHeader.encrypted", "application/json", Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

                //TODO: here I am removing the file and drive id from the stream but we need to resolve this by moving the file information to the server header
                var metadata = await _driveService.GetMetadata(file);

                //redact information
                metadata.File = InternalDriveFileId.Redacted();
                metadata.SenderDotYouId = string.Empty;
                metadata.AccessControlList = null;

                //redact the info by explicitly stating what we will keep
                //therefore, if a new attribute is added, it must be considered 
                //if it should be sent to the recipient
                var redactedMetadata = new FileMetadata()
                {
                    File = InternalDriveFileId.Redacted(),
                    Created = metadata.Created,
                    Updated = metadata.Updated,
                    AppData = metadata.AppData,
                    ContentType = metadata.ContentType,
                };

                var json = JsonConvert.SerializeObject(redactedMetadata);
                var stream = new MemoryStream(json.ToUtf8ByteArray());
                var metaDataStream = new StreamPart(stream, "metadata.encrypted", "application/json", Enum.GetName(MultipartHostTransferParts.Metadata));

                var payload = new StreamPart(await _driveService.GetPayloadStream(file), "payload.encrypted", "application/x-binary", Enum.GetName(MultipartHostTransferParts.Payload));

                //TODO: add additional error checking for files existing and successfully being opened, etc.

                //TODO: here we need to decrypt the token. 
                var decryptedClientAuthTokenBytes = transferInstructionSet.EncryptedClientAuthToken;
                var clientAuthToken = ClientAuthenticationToken.Parse(decryptedClientAuthTokenBytes.StringFromUTF8Bytes());

                var client = _dotYouHttpClientFactory.CreateClientWithAccessToken<ITransitHostHttpClient>(recipient, clientAuthToken, outboxItem.AppId);
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
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<RecipientTransferInstructionSetItem>(RecipientEncryptedTransferKeyHeaderCache, s => s.FindOne(r => r.Recipient == recipient && r.File == file));
            return item?.InstructionSet;
        }

        private async Task<TransitPublicKey> GetRecipientTransitPublicKey(DotYouIdentity recipient, bool lookupIfInvalid = true)
        {
            throw new NotImplementedException("use public key service");
        }
    }
}