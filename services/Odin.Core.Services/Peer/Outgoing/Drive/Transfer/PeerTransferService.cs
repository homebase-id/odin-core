using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Core.Services.Util;
using Odin.Core.Storage;
using Odin.Core.Time;
using Refit;
using Serilog;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Transfer
{
    public class PeerTransferService(
        OdinContextAccessor contextAccessor,
        IPeerOutbox peerOutbox,
        TenantSystemStorage tenantSystemStorage,
        IOdinHttpClientFactory odinHttpClientFactory,
        TenantContext tenantContext,
        CircleNetworkService circleNetworkService,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration,
        IDriveAclAuthorizationService driveAclAuthorizationService)
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService,
            contextAccessor, fileSystemResolver), IPeerTransferService
    {
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;
        private readonly TransferKeyEncryptionQueueService _transferKeyEncryptionQueueService = new(tenantSystemStorage);
        private readonly OdinContextAccessor _contextAccessor = contextAccessor;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory = odinHttpClientFactory;

        public async Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile,
            TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitWrite);

            Guard.Argument(options, nameof(options)).NotNull()
                .Require(o => o.Recipients.TrueForAll(r => r != tenantContext.HostOdinId));

            OdinValidationUtils.AssertValidRecipientList(options.Recipients);

            var sfo = new FileTransferOptions()
            {
                TransferFileType = transferFileType,
                // ClientAccessTokenSource = tokenSource,
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

        public async Task ProcessOutbox()
        {
            var batchSize = odinConfiguration.Transit.OutboxBatchSize;

            //Note: here we can prioritize outbox processing by drive if need be
            var page = await driveManager.GetDrives(PageOptions.All);

            foreach (var drive in page.Results)
            {
                var batch = await peerOutbox.GetBatchForProcessing(drive.Id, batchSize);
                // _logger.LogInformation($"Sending {batch.Results.Count} items from background controller");

                await this.SendBatchNow(batch);

                //was the batch successful?
            }
        }

        public async Task<Dictionary<string, PeerResponseCode>> SendDeleteFileRequest(GlobalTransitIdFileIdentifier remoteGlobalTransitIdentifier,
            FileTransferOptions fileTransferOptions, IEnumerable<string> recipients)
        {
            Dictionary<string, PeerResponseCode> result = new Dictionary<string, PeerResponseCode>();

            foreach (var recipient in recipients)
            {
                var r = (OdinId)recipient;

                var clientAccessToken = await ResolveClientAccessToken(r);

                var client = _odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(r, clientAccessToken.ToAuthenticationToken(),
                    fileSystemType: fileTransferOptions.FileSystemType);

                //TODO: change to accept a request object that has targetDrive and global transit id
                var httpResponse = await client.DeleteLinkedFile(new DeleteRemoteFileRequest()
                {
                    RemoteGlobalTransitIdFileIdentifier = remoteGlobalTransitIdentifier,
                    FileSystemType = fileTransferOptions.FileSystemType
                });

                if (httpResponse.IsSuccessStatusCode)
                {
                    var transitResponse = httpResponse.Content;
                    result.Add(recipient, transitResponse!.Code);
                }
                else
                {
                    result.Add(recipient, PeerResponseCode.Rejected);
                }
            }

            return result;
        }

        // 

        private EncryptedRecipientTransferInstructionSet CreateTransferInstructionSet(KeyHeader keyHeaderToBeEncrypted,
            ClientAccessToken clientAccessToken,
            TargetDrive targetDrive,
            TransferFileType transferFileType,
            FileSystemType fileSystemType, TransitOptions transitOptions)
        {
            var sharedSecret = clientAccessToken.SharedSecret;
            var iv = ByteArrayUtil.GetRndByteArray(16);
            var sharedSecretEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeaderToBeEncrypted, iv, ref sharedSecret);

            return new EncryptedRecipientTransferInstructionSet()
            {
                TargetDrive = targetDrive,
                TransferFileType = transferFileType,
                FileSystemType = fileSystemType,
                ContentsProvided = transitOptions.SendContents,
                SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
            };
        }

        private void AddToTransferKeyEncryptionQueue(OdinId recipient, InternalDriveFileId file)
        {
            var now = UnixTimeUtc.Now().milliseconds;
            var item = new PeerKeyEncryptionQueueItem()
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

        private async Task<List<FileTransferResult>> SendBatchNow(IEnumerable<TransitOutboxItem> items)
        {
            var tasks = new List<Task<FileTransferResult>>();
            var results = new List<FileTransferResult>();

            tasks.AddRange(items.Select(SendFileAsync));

            await Task.WhenAll(tasks);

            List<TransitOutboxItem> filesForDeletion = new List<TransitOutboxItem>();
            tasks.ForEach(task =>
            {
                var sendResult = task.Result;
                results.Add(sendResult);

                if (sendResult.Success)
                {
                    if (sendResult.OutboxItem.IsTransientFile)
                    {
                        filesForDeletion.Add(sendResult.OutboxItem);
                    }

                    peerOutbox.MarkComplete(sendResult.OutboxItem.Marker);
                }
                else
                {
                    peerOutbox.MarkFailure(sendResult.OutboxItem.Marker, sendResult.FailureReason.GetValueOrDefault());
                }
            });

            //TODO: optimization point; I need to see if this sort of deletion code is needed anymore; now that we have the transient temp drive
            foreach (var item in filesForDeletion)
            {
                var fs = _fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);
                await fs.Storage.HardDeleteLongTermFile(item.File);
            }

            return results;
        }

        private async Task<FileTransferResult> SendFileAsync(TransitOutboxItem outboxItem)
        {
            IDriveFileSystem fs = _fileSystemResolver.ResolveFileSystem(outboxItem.TransferInstructionSet.FileSystemType);

            OdinId recipient = outboxItem.Recipient;
            var file = outboxItem.File;
            var options = outboxItem.OriginalTransitOptions;

            var header = await fs.Storage.GetServerFileHeader(outboxItem.File);

            TransferFailureReason tfr = TransferFailureReason.UnknownError;
            bool success = false;
            PeerResponseCode peerResponseCode = PeerResponseCode.Rejected;
            try
            {
                // Enforce ACL at the last possible moment before shipping the file out of the identity; in case it changed
                if (!await driveAclAuthorizationService.IdentityHasPermission(recipient, header.ServerMetadata.AccessControlList))
                {
                    return new FileTransferResult()
                    {
                        File = file,
                        Recipient = recipient,
                        Timestamp = UnixTimeUtc.Now().milliseconds,
                        Success = false,
                        ShouldRetry = false,
                        FailureReason = TransferFailureReason.RecipientDoesNotHavePermissionToFileAcl,
                        OutboxItem = outboxItem
                    };
                }

                //look up transfer key
                var transferInstructionSet = outboxItem.TransferInstructionSet;

                if (null == transferInstructionSet)
                {
                    return new FileTransferResult()
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

                var shouldSendPayload = options.SendContents.HasFlag(SendContents.Payload);

                var decryptedClientAuthTokenBytes = outboxItem.EncryptedClientAuthToken;
                var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
                decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted

                if (options.UseAppNotification)
                {
                    transferInstructionSet.AppNotificationOptions = options.AppNotificationOptions;
                }

                var transferInstructionSetBytes = OdinSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray();
                var transferKeyHeaderStream = new StreamPart(
                    new MemoryStream(transferInstructionSetBytes),
                    "transferInstructionSet.encrypted", "application/json",
                    Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

                if (header.ServerMetadata.AllowDistribution == false)
                {
                    return new FileTransferResult()
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

                var sourceMetadata = header.FileMetadata;

                //redact the info by explicitly stating what we will keep
                //therefore, if a new attribute is added, it must be considered if it should be sent to the recipient
                var redactedMetadata = new FileMetadata()
                {
                    //TODO: here I am removing the file and drive id from the stream but we need
                    // to resolve this by moving the file information to the server header
                    File = InternalDriveFileId.Redacted(),
                    Created = sourceMetadata.Created,
                    Updated = sourceMetadata.Updated,
                    AppData = sourceMetadata.AppData,
                    IsEncrypted = sourceMetadata.IsEncrypted,
                    GlobalTransitId = options.OverrideRemoteGlobalTransitId.GetValueOrDefault(sourceMetadata.GlobalTransitId.GetValueOrDefault()),
                    ReactionPreview = sourceMetadata.ReactionPreview,
                    SenderOdinId = sourceMetadata.SenderOdinId,
                    ReferencedFile = sourceMetadata.ReferencedFile,
                    VersionTag = sourceMetadata.VersionTag,
                    Payloads = sourceMetadata.Payloads,
                    FileState = sourceMetadata.FileState,
                };

                var json = OdinSystemSerializer.Serialize(redactedMetadata);
                var stream = new MemoryStream(json.ToUtf8ByteArray());
                var metaDataStream = new StreamPart(stream, "metadata.encrypted", "application/json", Enum.GetName(MultipartHostTransferParts.Metadata));

                var additionalStreamParts = new List<StreamPart>();

                if (shouldSendPayload)
                {
                    foreach (var descriptor in redactedMetadata.Payloads ?? new List<PayloadDescriptor>())
                    {
                        var payloadKey = descriptor.Key;

                        string contentType = "application/unknown";

                        //TODO: consider what happens if the payload has been delete from disk
                        var p = await fs.Storage.GetPayloadStream(file, payloadKey, null);
                        var payloadStream = p.Stream;

                        var payload = new StreamPart(payloadStream, payloadKey, contentType, Enum.GetName(MultipartHostTransferParts.Payload));
                        additionalStreamParts.Add(payload);

                        foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                        {
                            var (thumbStream, thumbHeader) =
                                await fs.Storage.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight, descriptor.Key, descriptor.Uid);

                            var thumbnailKey =
                                $"{payloadKey}" +
                                $"{DriveFileUtility.TransitThumbnailKeyDelimiter}" +
                                $"{thumb.PixelWidth}" +
                                $"{DriveFileUtility.TransitThumbnailKeyDelimiter}" +
                                $"{thumb.PixelHeight}";

                            additionalStreamParts.Add(new StreamPart(thumbStream, thumbnailKey, thumbHeader.ContentType,
                                Enum.GetName(MultipartUploadParts.Thumbnail)));
                        }
                    }
                }

                var client = _odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient, clientAuthToken);
                var response = await client.SendHostToHost(transferKeyHeaderStream, metaDataStream, additionalStreamParts.ToArray());

                if (response.IsSuccessStatusCode)
                {
                    peerResponseCode = response.Content.Code;
                    switch (peerResponseCode)
                    {
                        case PeerResponseCode.AcceptedDirectWrite:
                        case PeerResponseCode.AcceptedIntoInbox:
                            success = true;
                            break;
                        case PeerResponseCode.QuarantinedPayload:
                        case PeerResponseCode.QuarantinedSenderNotConnected:
                        case PeerResponseCode.Rejected:
                            tfr = TransferFailureReason.RecipientServerRejected;
                            break;
                        case PeerResponseCode.AccessDenied:
                            tfr = TransferFailureReason.RecipientServerReturnedAccessDenied;
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
                // tfr = TransferFailureReason.CouldNotEncrypt;
                //TODO: logging
                throw;
            }

            return new FileTransferResult()
            {
                File = file,
                Recipient = recipient,
                Success = success,
                RecipientPeerResponseCode = peerResponseCode,
                ShouldRetry = true,
                FailureReason = tfr,
                Timestamp = UnixTimeUtc.Now().milliseconds,
                OutboxItem = outboxItem
            };
        }

        private async Task<(Dictionary<string, TransferStatus> transferStatus, IEnumerable<TransitOutboxItem>)> CreateOutboxItems(
            InternalDriveFileId internalFile,
            TransitOptions options,
            FileTransferOptions fileTransferOptions
        )
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);

            TargetDrive targetDrive = options.RemoteTargetDrive ?? (await driveManager.GetDrive(internalFile.DriveId, failIfInvalid: true)).TargetDriveInfo;

            var transferStatus = new Dictionary<string, TransferStatus>();
            var outboxItems = new List<TransitOutboxItem>();

            if (options.Recipients?.Contains(tenantContext.HostOdinId) ?? false)
            {
                throw new OdinClientException("Cannot transfer a file to the sender; what's the point?", OdinClientErrorCode.InvalidRecipient);
            }

            var header = await fs.Storage.GetServerFileHeader(internalFile);
            var storageKey = _contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(internalFile.DriveId);

            var keyHeader = header.FileMetadata.IsEncrypted ? header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey) : KeyHeader.Empty();
            storageKey.Wipe();

            foreach (var r in options.Recipients!)
            {
                var recipient = (OdinId)r;
                try
                {
                    //TODO: i need to resolve the token outside of transit, pass it in as options instead
                    var clientAuthToken = await ResolveClientAccessToken(recipient);

                    transferStatus.Add(recipient, TransferStatus.TransferKeyCreated);

                    //TODO: apply encryption before storing in the outbox
                    var encryptedClientAccessToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes();

                    outboxItems.Add(new TransitOutboxItem()
                    {
                        IsTransientFile = options.IsTransient,
                        File = internalFile,
                        Recipient = recipient,
                        OriginalTransitOptions = options,
                        EncryptedClientAuthToken = encryptedClientAccessToken,
                        TransferInstructionSet = this.CreateTransferInstructionSet(
                            keyHeader,
                            clientAuthToken,
                            targetDrive,
                            fileTransferOptions.TransferFileType,
                            fileTransferOptions.FileSystemType,
                            options)
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed while creating outbox item {ex.Message}");
                    AddToTransferKeyEncryptionQueue(recipient, internalFile);
                    transferStatus.Add(recipient, TransferStatus.AwaitingTransferKey);
                }
            }

            return (transferStatus, outboxItems);
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileLater(InternalDriveFileId internalFile,
            TransitOptions options, FileTransferOptions fileTransferOptions)
        {
            Guard.Argument(options, nameof(options)).NotNull()
                .Require(o => o.Recipients?.Any() ?? false)
                .Require(o => o.Recipients.TrueForAll(r => r != tenantContext.HostOdinId));

            //Since the owner is online (in this request) we can prepare a transfer key.  the outbox processor
            //will read the transfer key during the background send process

            var (transferStatus, outboxItems) = await CreateOutboxItems(internalFile, options, fileTransferOptions);
            await peerOutbox.Add(outboxItems);
            return transferStatus;
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileNow(InternalDriveFileId internalFile,
            TransitOptions transitOptions, FileTransferOptions fileTransferOptions)
        {
            Guard.Argument(transitOptions, nameof(transitOptions)).NotNull()
                .Require(o => o.Recipients?.Any() ?? false)
                .Require(o => o.Recipients.TrueForAll(r => r != tenantContext.HostOdinId));

            var (transferStatus, outboxItems) = await CreateOutboxItems(internalFile, transitOptions, fileTransferOptions);
            var sendResults = await this.SendBatchNow(outboxItems);

            foreach (var result in sendResults)
            {
                if (result.Success)
                {
                    switch (result.RecipientPeerResponseCode)
                    {
                        case PeerResponseCode.AcceptedIntoInbox:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.DeliveredToInbox;
                            break;
                        case PeerResponseCode.AcceptedDirectWrite:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.DeliveredToTargetDrive;
                            break;
                        default:
                            throw new OdinSystemException("Unhandled success scenario in transit");
                    }
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
                            await peerOutbox.Add(result.OutboxItem);
                            transferStatus[result.Recipient.DomainName] = TransferStatus.PendingRetry;
                            break;

                        case TransferFailureReason.RecipientServerError:
                        case TransferFailureReason.UnknownError:
                        case TransferFailureReason.RecipientServerRejected:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.TotalRejectionClientShouldRetry;
                            break;

                        case TransferFailureReason.RecipientDoesNotHavePermissionToFileAcl:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.RecipientDoesNotHavePermissionToFileAcl;
                            break;

                        case TransferFailureReason.FileDoesNotAllowDistribution:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.FileDoesNotAllowDistribution;
                            break;

                        case TransferFailureReason.RecipientServerReturnedAccessDenied:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.RecipientReturnedAccessDenied;

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            return transferStatus;
        }
    }
}