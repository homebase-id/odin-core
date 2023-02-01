using Refit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit.Incoming;
using Youverse.Core.Storage;

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
        private readonly ITenantSystemStorage _tenantSystemStorage;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly TenantContext _tenantContext;
        private readonly ICircleNetworkService _circleNetworkService;
        private readonly FollowerService _followerService;
        private readonly IPublicKeyService _publicKeyService;


        public TransitService(DotYouContextAccessor contextAccessor,
            ILogger<TransitService> logger,
            IOutboxService outboxService,
            IDriveService driveService,
            ITransferKeyEncryptionQueueService transferKeyEncryptionQueueService,
            ITransitBoxService transitBoxService,
            ITenantSystemStorage tenantSystemStorage,
            IDotYouHttpClientFactory dotYouHttpClientFactory, TenantContext tenantContext,
            ICircleNetworkService circleNetworkService, IPublicKeyService publicKeyService, FollowerService followerService) : base()
        {
            _contextAccessor = contextAccessor;
            _outboxService = outboxService;
            _driveService = driveService;
            _transferKeyEncryptionQueueService = transferKeyEncryptionQueueService;
            _transitBoxService = transitBoxService;
            _tenantSystemStorage = tenantSystemStorage;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _tenantContext = tenantContext;
            _circleNetworkService = circleNetworkService;
            _publicKeyService = publicKeyService;
            _followerService = followerService;
            _logger = logger;
        }


        public async Task AcceptTransfer(InternalDriveFileId file, uint publicKeyCrc)
        {
            _logger.LogInformation($"TransitService.AcceptTransfer temp fileId:{file.FileId} driveId:{file.DriveId}");

            var item = new TransferBoxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = this._contextAccessor.GetCurrent().Caller.DotYouId,
                PublicKeyCrc = publicKeyCrc,

                InstructionType = TransferInstructionType.SaveFile,
                DriveId = file.DriveId,
                FileId = file.FileId
            };

            //Note: the inbox service will send the notification
            await _transitBoxService.Add(item);
        }

        public async Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile,
            TransitOptions options, TransferFileType transferFileType, ClientAccessTokenSource tokenSource = ClientAccessTokenSource.Circle)
        {
            Guard.Argument(options, nameof(options)).NotNull()
                .Require(o => o.Recipients?.Any() ?? false)
                .Require(o => o.Recipients.TrueForAll(r => r != _tenantContext.HostDotYouId));


            if (options.Schedule == ScheduleOptions.SendNowAwaitResponse)
            {
                //send now
                return await SendFileNow(internalFile, options, transferFileType, tokenSource);
            }
            else
            {
                return await SendFileLater(internalFile, options, transferFileType, tokenSource);
            }
        }

        // 

        private RsaEncryptedRecipientTransferInstructionSet CreateTransferInstructionSet(byte[] recipientPublicKeyDer,
            KeyHeader keyHeaderToBeEncrypted,
            ClientAuthenticationToken clientAuthenticationToken, InternalDriveFileId internalFile,
            DotYouIdentity recipient, TargetDrive targetDrive, TransferFileType transferFileType)
        {
            //TODO: need to review how to decrypt the private key on the recipient side
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(recipientPublicKeyDer);

            var combinedKey = keyHeaderToBeEncrypted.Combine();
            var rsaEncryptedKeyHeader = publicKey.Encrypt(combinedKey.GetKey());
            combinedKey.Wipe();

            //TODO: need to encrypt the client access token here with something on my server side (therefore, we cannot use RSA encryption)
            var encryptedClientAccessToken = clientAuthenticationToken.ToString().ToUtf8ByteArray();

            return new RsaEncryptedRecipientTransferInstructionSet()
            {
                PublicKeyCrc = publicKey.crc32c,
                EncryptedAesKeyHeader = rsaEncryptedKeyHeader,
                EncryptedClientAuthToken = encryptedClientAccessToken, //TODO: need to move this to be directly on the outbox item
                TargetDrive = targetDrive,
                TransferFileType = transferFileType
            };
        }

        private void AddToTransferKeyEncryptionQueue(DotYouIdentity recipient, InternalDriveFileId file)
        {
            var now = UnixTimeUtc.Now().milliseconds;
            var item = new TransitKeyEncryptionQueueItem()
            {
                Id = GuidId.NewId(),
                FileId = file.FileId,
                Recipient = recipient,
                FirstAddedTimestampMs = now,
                Attempts = 1,
                LastAttemptTimestampMs = now
            };

            _transferKeyEncryptionQueueService.Enqueue(item);
        }

        public async Task<List<SendResult>> SendBatchNow(IEnumerable<OutboxItem> items)
        {
            var tasks = new List<Task<SendResult>>();
            var results = new List<SendResult>();

            tasks.AddRange(items.Select(SendAsync));

            await Task.WhenAll(tasks);

            List<InternalDriveFileId> filesForDeletion = new List<InternalDriveFileId>();
            tasks.ForEach(task =>
            {
                var sendResult = task.Result;
                results.Add(sendResult);

                if (sendResult.Success)
                {
                    if (sendResult.OutboxItem.IsTransientFile)
                    {
                        filesForDeletion.Add(sendResult.OutboxItem.File);
                    }

                    _outboxService.MarkComplete(sendResult.OutboxItem.Marker);
                }
                else
                {
                    _outboxService.MarkFailure(sendResult.OutboxItem.Marker,
                        sendResult.FailureReason.GetValueOrDefault());
                }
            });

            foreach (var fileId in filesForDeletion)
            {
                //TODO: add logging?
                await _driveService.HardDeleteLongTermFile(fileId);
            }

            return results;
        }

        public async Task ProcessOutbox(int batchSize)
        {
            //Note: here we can prioritize outbox processing by drive if need be
            var page = await _driveService.GetDrives(PageOptions.All);

            foreach (var drive in page.Results)
            {
                var batch = await _outboxService.GetBatchForProcessing(drive.Id, batchSize);
                // _logger.LogInformation($"Sending {batch.Results.Count} items from background controller");

                await this.SendBatchNow(batch);

                //was the batch successful?
            }
        }

        public async Task<Dictionary<string, TransitResponseCode>> SendDeleteLinkedFileRequest(Guid driveId,
            Guid globalTransitId, IEnumerable<string> recipients)
        {
            Dictionary<string, TransitResponseCode> result = new Dictionary<string, TransitResponseCode>();

            var targetDrive = (await _driveService.GetDrive(driveId, true)).TargetDriveInfo;
            foreach (var recipient in recipients)
            {
                var r = (DotYouIdentity)recipient;
                var clientAuthToken = _circleNetworkService.GetConnectionAuthToken(r).GetAwaiter().GetResult();
                var client =
                    _dotYouHttpClientFactory.CreateClientUsingAccessToken<ITransitHostHttpClient>(r, clientAuthToken);

                //TODO: change to accept a request object that has targetDrive and global transit id
                var httpResponse = await client.DeleteLinkedFile(new DeleteLinkedFileTransitRequest()
                {
                    TargetDrive = targetDrive,
                    GlobalTransitId = globalTransitId
                });

                if (httpResponse.IsSuccessStatusCode)
                {
                    var transitResponse = httpResponse.Content;
                    result.Add(recipient, transitResponse!.Code);
                }
                else
                {
                    result.Add(recipient, TransitResponseCode.Rejected);
                }
            }

            return result;
        }

        public async Task AcceptDeleteLinkedFileRequest(Guid driveId, Guid globalTransitId)
        {
            _logger.LogInformation($"TransitService.AcceptDeleteLinkedFileRequest {globalTransitId} on target drive [{driveId}]");

            uint publicKeyCrc = 0; //TODO: we need to encrypt the delete linked file request using the public key or the shared secret

            var item = new TransferBoxItem()
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = this._contextAccessor.GetCurrent().Caller.DotYouId,
                PublicKeyCrc = publicKeyCrc,

                InstructionType = TransferInstructionType.DeleteLinkedFile,
                DriveId = driveId,
                GlobalTransitId = globalTransitId
            };

            //Note: the inbox service will send the notification
            await _transitBoxService.Add(item);
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
                var transferInstructionSet = outboxItem.TransferInstructionSet;
                if (null == transferInstructionSet)
                {
                    return new SendResult()
                    {
                        File = file,
                        Recipient = recipient,
                        Timestamp = UnixTimeUtc.Now().milliseconds,
                        Success = false,
                        FailureReason = TransferFailureReason.EncryptedTransferInstructionSetNotAvailable,
                        OutboxItem = outboxItem
                    };
                }

                //
                // Critical: Get the client auth token, then redact it so it's not sent 
                //
                var decryptedClientAuthTokenBytes = transferInstructionSet.EncryptedClientAuthToken;
                var clientAuthToken = ClientAuthenticationToken.Parse(decryptedClientAuthTokenBytes.ToStringFromUtf8Bytes());
                decryptedClientAuthTokenBytes.WriteZeros();
                transferInstructionSet.EncryptedClientAuthToken.WriteZeros(); //never send the client auth token; even if encrypted

                var transferKeyHeaderBytes = DotYouSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray();
                var transferKeyHeaderStream = new StreamPart(new MemoryStream(transferKeyHeaderBytes),
                    "transferKeyHeader.encrypted", "application/json",
                    Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

                var header = await _driveService.GetServerFileHeader(file);
                var metadata = header.FileMetadata;

                //redact the info by explicitly stating what we will keep
                //therefore, if a new attribute is added, it must be considered if it should be sent to the recipient
                var redactedMetadata = new FileMetadata()
                {
                    //TODO: here I am removing the file and drive id from the stream but we need to resolve this by moving the file information to the server header
                    File = InternalDriveFileId.Redacted(),
                    Created = metadata.Created,
                    Updated = metadata.Updated,
                    AppData = metadata.AppData,
                    PayloadIsEncrypted = metadata.PayloadIsEncrypted,
                    ContentType = metadata.ContentType,
                    GlobalTransitId = metadata.GlobalTransitId,
                    SenderDotYouId = string.Empty,
                    OriginalRecipientList = null,
                };

                var json = DotYouSystemSerializer.Serialize(redactedMetadata);
                var stream = new MemoryStream(json.ToUtf8ByteArray());
                var metaDataStream = new StreamPart(stream, "metadata.encrypted", "application/json", Enum.GetName(MultipartHostTransferParts.Metadata));

                var payloadStream = metadata.AppData.ContentIsComplete
                    ? Stream.Null
                    : await _driveService.GetPayloadStream(file);
                var payload = new StreamPart(payloadStream, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartHostTransferParts.Payload));

                var thumbnails = new List<StreamPart>();
                foreach (var thumb in redactedMetadata.AppData?.AdditionalThumbnails ?? new List<ImageDataHeader>())
                {
                    var thumbStream = await _driveService.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight);
                    thumbnails.Add(new StreamPart(thumbStream, thumb.GetFilename(), thumb.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
                }

                var client = _dotYouHttpClientFactory.CreateClientUsingAccessToken<ITransitHostHttpClient>(recipient, clientAuthToken);
                var response = client.SendHostToHost(transferKeyHeaderStream, metaDataStream, payload, thumbnails.ToArray()).ConfigureAwait(false).GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    var transitResponse = response.Content;

                    switch (transitResponse.Code)
                    {
                        case TransitResponseCode.Accepted:
                            success = true;
                            break;
                        case TransitResponseCode.QuarantinedPayload:
                        case TransitResponseCode.QuarantinedSenderNotConnected:
                        case TransitResponseCode.Rejected:
                            tfr = TransferFailureReason.RecipientServerRejected;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else
                {
                    tfr = TransferFailureReason.RecipientServerError;
                }
            }
            catch (EncryptionException)
            {
                tfr = TransferFailureReason.CouldNotEncrypt;
                //TODO: logging
                throw;
            }

            return new SendResult()
            {
                File = file,
                Recipient = recipient,
                Success = success,
                FailureReason = tfr,
                Timestamp = UnixTimeUtc.Now().milliseconds,
                OutboxItem = outboxItem
            };
        }

        private async Task<(Dictionary<string, TransferStatus> transferStatus, IEnumerable<OutboxItem>)> CreateOutboxItems(InternalDriveFileId internalFile, TransitOptions options,
            TransferFileType transferFileType, ClientAccessTokenSource clientAccessTokenSource)
        {
            if (options.SendContents != SendContents.All)
            {
                throw new NotImplementedException("TODO: implement partial sends for feed drive support");
            }

            var drive = await _driveService.GetDrive(internalFile.DriveId, failIfInvalid: true);

            var transferStatus = new Dictionary<string, TransferStatus>();
            var outboxItems = new List<OutboxItem>();

            var header = await _driveService.GetServerFileHeader(internalFile);
            var storageKey = _contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(internalFile.DriveId);
            var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
            storageKey.Wipe();

            foreach (var r in options.Recipients)
            {
                var recipient = (DotYouIdentity)r;
                try
                {
                    //TODO: decide if we should lookup the public key from the recipients host if not cached or just drop the item in the queue
                    var pk = await _publicKeyService.GetRecipientOfflinePublicKey(recipient, true, false);
                    if (null == pk)
                    {
                        AddToTransferKeyEncryptionQueue(recipient, internalFile);
                        transferStatus.Add(recipient, TransferStatus.AwaitingTransferKey);
                        continue;
                    }

                    //TODO: i need to resolve the token outside of transit, pass it in as options instead
                    var clientAuthToken = await ResolveClientAccessToken(recipient, clientAccessTokenSource);

                    transferStatus.Add(recipient, TransferStatus.TransferKeyCreated);
                    outboxItems.Add(new OutboxItem()
                    {
                        IsTransientFile = options.IsTransient,
                        File = internalFile,
                        Recipient = (DotYouIdentity)r,
                        TransferInstructionSet = this.CreateTransferInstructionSet(pk.publicKey, keyHeader,
                            clientAuthToken.ToAuthenticationToken(), internalFile, recipient, drive.TargetDriveInfo, transferFileType)
                    });
                }
                catch (Exception ex)
                {
                    AddToTransferKeyEncryptionQueue(recipient, internalFile);
                    transferStatus.Add(recipient, TransferStatus.AwaitingTransferKey);
                }
            }

            return (transferStatus, outboxItems);
        }

        private async Task<ClientAccessToken> ResolveClientAccessToken(DotYouIdentity recipient, ClientAccessTokenSource source)
        {
            if (source == ClientAccessTokenSource.Circle)
            {
                var icr = await _circleNetworkService.GetIdentityConnectionRegistration(recipient);
                return icr.CreateClientAccessToken();
            }

            if (source == ClientAccessTokenSource.Follower)
            {
                var def = await _followerService.GetFollower(recipient);
                return def.CreateClientAccessToken();
            }
            
            throw new ArgumentException("Invalid ClientAccessTokenSource");

        }

        private async Task<Dictionary<string, TransferStatus>> SendFileLater(InternalDriveFileId internalFile,
            TransitOptions options, TransferFileType transferFileType, ClientAccessTokenSource clientAccessTokenSource)
        {
            Guard.Argument(options, nameof(options)).NotNull()
                .Require(o => o.Recipients?.Any() ?? false)
                .Require(o => o.Recipients.TrueForAll(r => r != _tenantContext.HostDotYouId));

            //Since the owner is online (in this request) we can prepare a transfer key.  the outbox processor
            //will read the transfer key during the background send process

            var (transferStatus, outboxItems) = await CreateOutboxItems(internalFile, options, transferFileType, clientAccessTokenSource);
            await _outboxService.Add(outboxItems);
            return transferStatus;
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileNow(InternalDriveFileId internalFile,
            TransitOptions options, TransferFileType transferFileType, ClientAccessTokenSource clientAccessTokenSource)
        {
            Guard.Argument(options, nameof(options)).NotNull()
                .Require(o => o.Recipients?.Any() ?? false)
                .Require(o => o.Recipients.TrueForAll(r => r != _tenantContext.HostDotYouId));

            var (transferStatus, outboxItems) = await CreateOutboxItems(internalFile, options, transferFileType, clientAccessTokenSource);
            var sendResults = await this.SendBatchNow(outboxItems);

            foreach (var result in sendResults)
            {
                if (result.Success)
                {
                    transferStatus[result.Recipient.Id] = TransferStatus.Delivered;
                }
                else
                {
                    switch (result.FailureReason)
                    {
                        case TransferFailureReason.TransitPublicKeyInvalid:
                        case TransferFailureReason.RecipientPublicKeyInvalid:
                        case TransferFailureReason.CouldNotEncrypt:
                        case TransferFailureReason.EncryptedTransferInstructionSetNotAvailable:
                            //enqueue the failures into the outbox
                            await _outboxService.Add(result.OutboxItem);
                            transferStatus[result.Recipient.Id] = TransferStatus.PendingRetry;
                            break;

                        case TransferFailureReason.RecipientServerError:
                        case TransferFailureReason.UnknownError:
                        case TransferFailureReason.RecipientServerRejected:
                            transferStatus[result.Recipient.Id] = TransferStatus.TotalRejectionClientShouldRetry;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            return transferStatus;
        }
    }

    /// <summary>
    /// Specifies the type of instruction incoming from another identity 
    /// </summary>
    public enum TransferInstructionType
    {
        None,
        DeleteLinkedFile,
        SaveFile
    }
}