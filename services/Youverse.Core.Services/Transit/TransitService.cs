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
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit.Incoming;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit
{
    internal class SendFileOptions
    {
        public TransferFileType TransferFileType { get; set; }
        public ClientAccessTokenSource ClientAccessTokenSource { get; set; }
        public FileSystemType FileSystemType { get; set; }
    }

    public class TransitService : TransitServiceBase<ITransitService>, ITransitService
    {
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly DriveManager _driveManager;
        private readonly ITransitOutbox _transitOutbox;
        private readonly TransferKeyEncryptionQueueService _transferKeyEncryptionQueueService;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly TenantContext _tenantContext;
        private readonly ICircleNetworkService _circleNetworkService;
        private readonly FollowerService _followerService;
        private readonly IPublicKeyService _publicKeyService;

        private readonly IDriveFileSystem _fileSystem;

        public TransitService(
            DotYouContextAccessor contextAccessor,
            ITransitOutbox transitOutbox,
            ITenantSystemStorage tenantSystemStorage,
            IDotYouHttpClientFactory dotYouHttpClientFactory,
            TenantContext tenantContext,
            ICircleNetworkService circleNetworkService,
            IPublicKeyService publicKeyService,
            FollowerService followerService,
            DriveManager driveManager,
            IDriveFileSystem fileSystem,
            FileSystemResolver fileSystemResolver)
        {
            _contextAccessor = contextAccessor;
            _transitOutbox = transitOutbox;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _tenantContext = tenantContext;
            _circleNetworkService = circleNetworkService;
            _publicKeyService = publicKeyService;
            _followerService = followerService;
            _driveManager = driveManager;
            _fileSystem = fileSystem;
            _fileSystemResolver = fileSystemResolver;

            _transferKeyEncryptionQueueService = new TransferKeyEncryptionQueueService(tenantSystemStorage);
        }


        public async Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile,
            TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType, ClientAccessTokenSource tokenSource = ClientAccessTokenSource.Circle)
        {
            Guard.Argument(options, nameof(options)).NotNull()
                .Require(o => o.Recipients?.Any() ?? false)
                .Require(o => o.Recipients.TrueForAll(r => r != _tenantContext.HostDotYouId));

            var sfo = new SendFileOptions()
            {
                TransferFileType = transferFileType,
                ClientAccessTokenSource = tokenSource,
                FileSystemType = fileSystemType
            };

            if (options.Schedule == ScheduleOptions.SendNowAwaitResponse)
            {
                //send now
                return await SendFileNow(internalFile, options, sfo);
            }
            else
            {
                return await SendFileLater(internalFile, options, sfo);
            }
        }

        // 

        private RsaEncryptedRecipientTransferInstructionSet CreateTransferInstructionSet(byte[] recipientPublicKeyDer,
            KeyHeader keyHeaderToBeEncrypted,
            ClientAccessToken clientAccessToken,
            TargetDrive targetDrive,
            TransferFileType transferFileType, FileSystemType fileSystemType)
        {
            //TODO: need to review how to decrypt the private key on the recipient side
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(recipientPublicKeyDer);

            var combinedKey = keyHeaderToBeEncrypted.Combine();
            var rsaEncryptedKeyHeader = publicKey.Encrypt(combinedKey.GetKey());
            combinedKey.Wipe();

            return new RsaEncryptedRecipientTransferInstructionSet()
            {
                PublicKeyCrc = publicKey.crc32c,
                EncryptedAesKeyHeader = rsaEncryptedKeyHeader,
                TargetDrive = targetDrive,
                TransferFileType = transferFileType,
                FileSystemType = fileSystemType
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

        public async Task ProcessOutbox(int batchSize)
        {
            //Note: here we can prioritize outbox processing by drive if need be
            var page = await _driveManager.GetDrives(PageOptions.All);

            foreach (var drive in page.Results)
            {
                var batch = await _transitOutbox.GetBatchForProcessing(drive.Id, batchSize);
                // _logger.LogInformation($"Sending {batch.Results.Count} items from background controller");

                await this.SendBatchNow(batch);

                //was the batch successful?
            }
        }

        public async Task<Dictionary<string, TransitResponseCode>> SendDeleteLinkedFileRequest(Guid driveId,
            Guid globalTransitId, FileSystemType fileSystemType, IEnumerable<string> recipients)
        {
            Dictionary<string, TransitResponseCode> result = new Dictionary<string, TransitResponseCode>();

            var targetDrive = (await _driveManager.GetDrive(driveId, true)).TargetDriveInfo;
            foreach (var recipient in recipients)
            {
                var r = (DotYouIdentity)recipient;
                var clientAuthToken = _circleNetworkService.GetConnectionAuthToken(r).GetAwaiter().GetResult();
                var client = _dotYouHttpClientFactory.CreateClientUsingAccessToken<ITransitHostHttpClient>(r, clientAuthToken);

                //TODO: change to accept a request object that has targetDrive and global transit id
                var httpResponse = await client.DeleteLinkedFile(new DeleteLinkedFileTransitRequest()
                {
                    TargetDrive = targetDrive,
                    GlobalTransitId = globalTransitId,
                    FileSystemType = fileSystemType
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

        private async Task<List<SendResult>> SendBatchNow(IEnumerable<TransitOutboxItem> items)
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

                    _transitOutbox.MarkComplete(sendResult.OutboxItem.Marker);
                }
                else
                {
                    _transitOutbox.MarkFailure(sendResult.OutboxItem.Marker, sendResult.FailureReason.GetValueOrDefault());
                }
            });

            foreach (var fileId in filesForDeletion)
            {
                //TODO: add logging?
                await _fileSystem.Storage.HardDeleteLongTermFile(fileId);
            }

            return results;
        }

        private async Task<SendResult> SendAsync(TransitOutboxItem outboxItem)
        {
            IDriveFileSystem fs = _fileSystemResolver.ResolveFileSystem(outboxItem.TransferInstructionSet.FileSystemType);

            DotYouIdentity recipient = outboxItem.Recipient;
            var file = outboxItem.File;
            var options = outboxItem.OriginalTransitOptions;


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
                        ShouldRetry = true,
                        FailureReason = TransferFailureReason.EncryptedTransferInstructionSetNotAvailable,
                        OutboxItem = outboxItem
                    };
                }

                var decryptedClientAuthTokenBytes = outboxItem.EncryptedClientAuthToken;
                var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
                decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

                var transferKeyHeaderBytes = DotYouSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray();
                var transferKeyHeaderStream = new StreamPart(
                    new MemoryStream(transferKeyHeaderBytes),
                    "transferKeyHeader.encrypted", "application/json",
                    Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

                var header = await fs.Storage.GetServerFileHeader(file);
                if (header.ServerMetadata.AllowDistribution == false)
                {
                    return new SendResult()
                    {
                        File = file,
                        Recipient = recipient,
                        Timestamp = UnixTimeUtc.Now().milliseconds,
                        Success = false,
                        ShouldRetry = false,
                        FailureReason = TransferFailureReason.FileDoesNotAllowDistribution,
                        OutboxItem = outboxItem
                    };
                }

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

                var additionalStreamParts = new List<StreamPart>();

                if (options.SendContents.HasFlag(SendContents.Payload))
                {
                    var payloadStream = metadata.AppData.ContentIsComplete
                        ? Stream.Null
                        : await fs.Storage.GetPayloadStream(file);
                    var payload = new StreamPart(payloadStream, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartHostTransferParts.Payload));
                    additionalStreamParts.Add(payload);
                }

                if (options.SendContents.HasFlag(SendContents.Thumbnails))
                {
                    foreach (var thumb in redactedMetadata.AppData?.AdditionalThumbnails ?? new List<ImageDataHeader>())
                    {
                        var thumbStream = await fs.Storage.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight);
                        additionalStreamParts.Add(new StreamPart(thumbStream, thumb.GetFilename(), thumb.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
                    }
                }

                var client = _dotYouHttpClientFactory.CreateClientUsingAccessToken<ITransitHostHttpClient>(recipient, clientAuthToken);
                var response = await client.SendHostToHost(transferKeyHeaderStream, metaDataStream, additionalStreamParts.ToArray());

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
                ShouldRetry = true,
                FailureReason = tfr,
                Timestamp = UnixTimeUtc.Now().milliseconds,
                OutboxItem = outboxItem
            };
        }

        private async Task<(Dictionary<string, TransferStatus> transferStatus, IEnumerable<TransitOutboxItem>)> CreateOutboxItems(
            InternalDriveFileId internalFile,
            TransitOptions options,
            SendFileOptions sendFileOptions
        )
        {
            var fs = _fileSystemResolver.ResolveFileSystem(sendFileOptions.FileSystemType);

            TargetDrive targetDrive = options.OverrideTargetDrive ?? (await _driveManager.GetDrive(internalFile.DriveId, failIfInvalid: true)).TargetDriveInfo;

            var transferStatus = new Dictionary<string, TransferStatus>();
            var outboxItems = new List<TransitOutboxItem>();

            if (options.Recipients?.Contains(_tenantContext.HostDotYouId) ?? false)
            {
                throw new YouverseClientException("Cannot transfer a file to the sender; what's the point?", YouverseClientErrorCode.InvalidRecipient);
            }

            var header = await fs.Storage.GetServerFileHeader(internalFile);
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
                    var clientAuthToken = await ResolveClientAccessToken(recipient, sendFileOptions.ClientAccessTokenSource);

                    transferStatus.Add(recipient, TransferStatus.TransferKeyCreated);

                    //TODO apply encryption
                    var encryptedClientAccessToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes();

                    outboxItems.Add(new TransitOutboxItem()
                    {
                        IsTransientFile = options.IsTransient,
                        File = internalFile,
                        Recipient = (DotYouIdentity)r,
                        OriginalTransitOptions = options,
                        EncryptedClientAuthToken = encryptedClientAccessToken,
                        TransferInstructionSet = this.CreateTransferInstructionSet(
                            pk.publicKey,
                            keyHeader,
                            clientAuthToken,
                            targetDrive,
                            sendFileOptions.TransferFileType,
                            sendFileOptions.FileSystemType)
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

            if (source == ClientAccessTokenSource.DataSubscription)
            {
                var def = await _followerService.GetFollower(recipient);
                return def.CreateClientAccessToken();
            }

            throw new ArgumentException("Invalid ClientAccessTokenSource");
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileLater(InternalDriveFileId internalFile,
            TransitOptions options, SendFileOptions sendFileOptions)
        {
            Guard.Argument(options, nameof(options)).NotNull()
                .Require(o => o.Recipients?.Any() ?? false)
                .Require(o => o.Recipients.TrueForAll(r => r != _tenantContext.HostDotYouId));

            //Since the owner is online (in this request) we can prepare a transfer key.  the outbox processor
            //will read the transfer key during the background send process

            var (transferStatus, outboxItems) = await CreateOutboxItems(internalFile, options, sendFileOptions);
            await _transitOutbox.Add(outboxItems);
            return transferStatus;
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileNow(InternalDriveFileId internalFile,
            TransitOptions transitOptions, SendFileOptions sendFileOptions)
        {
            Guard.Argument(transitOptions, nameof(transitOptions)).NotNull()
                .Require(o => o.Recipients?.Any() ?? false)
                .Require(o => o.Recipients.TrueForAll(r => r != _tenantContext.HostDotYouId));

            var (transferStatus, outboxItems) = await CreateOutboxItems(internalFile, transitOptions, sendFileOptions);
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
                            await _transitOutbox.Add(result.OutboxItem);
                            transferStatus[result.Recipient.Id] = TransferStatus.PendingRetry;
                            break;

                        case TransferFailureReason.RecipientServerError:
                        case TransferFailureReason.UnknownError:
                        case TransferFailureReason.RecipientServerRejected:
                            transferStatus[result.Recipient.Id] = TransferStatus.TotalRejectionClientShouldRetry;
                            break;

                        case TransferFailureReason.FileDoesNotAllowDistribution:
                            transferStatus[result.Recipient.Id] = TransferStatus.FileDoesNotAllowDistribution;
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