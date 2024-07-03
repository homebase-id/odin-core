using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public class PeerOutgoingTransferService(
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        TenantContext tenantContext,
        CircleNetworkService circleNetworkService,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration,
        ServerSystemStorage serverSystemStorage,
        ILogger<PeerOutgoingTransferService> logger,
        PeerOutboxProcessorAsync outboxProcessorAsync
    )
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver), IPeerOutgoingTransferService
    {
        private readonly IOdinHttpClientFactory _odinHttpClientFactory = odinHttpClientFactory;
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;

        public async Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile,
            TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitWrite);

            OdinValidationUtils.AssertIsTrue(options.Recipients.TrueForAll(r => r != tenantContext.HostOdinId), "You cannot send a file to yourself");
            OdinValidationUtils.AssertValidRecipientList(options.Recipients);

            var sfo = new FileTransferOptions()
            {
                TransferFileType = transferFileType,
                FileSystemType = fileSystemType
            };

            var tenant = tenantContext.HostOdinId;
            serverSystemStorage.EnqueueJob(tenant, CronJobType.ReconcileInboxOutbox, tenant.DomainName.ToLower().ToUtf8ByteArray(), UnixTimeUtc.Now());

            var priority = options.Priority switch
            {
                OutboxPriority.High => 1000,
                OutboxPriority.Medium => 2000,
                _ => 3000
            };

            var (outboxStatus, outboxItems) = await CreateOutboxItems(internalFile, options, sfo, odinContext, priority, cn);

            //TODO: change this to a batch update of the transfer history
            foreach (var item in outboxItems)
            {
                var fs = _fileSystemResolver.ResolveFileSystem(item.State.TransferInstructionSet.FileSystemType);
                await fs.Storage.UpdateTransferHistory(internalFile, item.Recipient, new UpdateTransferHistoryData() { IsInOutbox = true }, odinContext, cn);
                await peerOutbox.AddItem(item, cn);
            }

            _ = outboxProcessorAsync.StartOutboxProcessingAsync(odinContext, cn);

            return outboxStatus;
        }

        public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(
            GlobalTransitIdFileIdentifier remoteGlobalTransitIdFileIdentifier,
            FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var fileId = new InternalDriveFileId()
            {
                FileId = remoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                DriveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.TransientTempDrive)
            };

            var result = await EnqueueDeletes(fileId, remoteGlobalTransitIdFileIdentifier, fileTransferOptions, recipients, odinContext, cn);

            return result;
        }

        public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(
            InternalDriveFileId fileId,
            FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);
            var header = await fs.Storage.GetServerFileHeader(fileId, odinContext, cn);

            if (null == header)
            {
                throw new OdinClientException("File not found", OdinClientErrorCode.InvalidFile);
            }

            var remoteGlobalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = odinContext.PermissionsContext.GetTargetDrive(header.FileMetadata.File.DriveId)
            };

            return await EnqueueDeletes(fileId, remoteGlobalTransitIdFileIdentifier, fileTransferOptions, recipients, odinContext, cn);
        }


        public async Task<SendReadReceiptResult> SendReadReceipt(List<InternalDriveFileId> files, IOdinContext odinContext,
            DatabaseConnection cn,
            FileSystemType fileSystemType)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileSystemType);

            // This is all ugly mapping code but 🤷
            var intermediateResults = new List<(ExternalFileIdentifier File, SendReadReceiptResultRecipientStatusItem StatusItem)>();
            foreach (var fileId in files)
            {
                var externalFile = new ExternalFileIdentifier()
                {
                    FileId = fileId.FileId,
                    TargetDrive = odinContext.PermissionsContext.GetTargetDrive(fileId.DriveId)
                };

                var header = await fs.Storage.GetServerFileHeader(fileId, odinContext, cn);

                try
                {
                    if (header == null)
                    {
                        throw new OdinClientException("Invalid File", OdinClientErrorCode.InvalidFile);
                    }

                    if (string.IsNullOrEmpty(header.FileMetadata.SenderOdinId) || string.IsNullOrWhiteSpace(header.FileMetadata.SenderOdinId))
                    {
                        throw new OdinClientException("File does not have a sender", OdinClientErrorCode.FileDoesNotHaveSender);
                    }

                    if (header.FileMetadata.GlobalTransitId == null)
                    {
                        throw new OdinClientException("File does not have global transit id", OdinClientErrorCode.MissingGlobalTransitId);
                    }

                    var statusItem = await SendReadReceiptToRecipient(header, fileId, odinContext, cn, fileSystemType);
                    intermediateResults.Add((externalFile, statusItem));
                }
                catch (OdinClientException oce)
                {
                    intermediateResults.Add((externalFile, new SendReadReceiptResultRecipientStatusItem()
                    {
                        Recipient = string.IsNullOrEmpty(header?.FileMetadata?.SenderOdinId) ? null : (OdinId)header.FileMetadata.SenderOdinId,
                        Status = SendReadReceiptResultStatus.LocalIdentityReturnedBadRequest
                    }));

                    logger.LogWarning(oce, "A client exception was detected while sending a read receipt for file {file};" +
                                           "we are logging this client exception since the client is another identity", fileId);
                }
                catch (Exception e)
                {
                    intermediateResults.Add((externalFile, new SendReadReceiptResultRecipientStatusItem()
                    {
                        Recipient = string.IsNullOrEmpty(header?.FileMetadata?.SenderOdinId) ? null : (OdinId)header.FileMetadata.SenderOdinId,
                        Status = SendReadReceiptResultStatus.SenderServerHadAnInternalError
                    }));

                    logger.LogWarning(e, "General exception occured while sending a read receipt file:{file}", fileId);
                }
            }

            var results = new List<SendReadReceiptResultFileItem>();

            // This, too, is all ugly mapping code but 🤷
            foreach (var item in intermediateResults.GroupBy(i => i.File))
            {
                results.Add(new SendReadReceiptResultFileItem
                {
                    File = item.Key,
                    Status = item.Select(i => i.StatusItem).ToList()
                });
            }

            return new SendReadReceiptResult()
            {
                Results = results
            };
        }

        // 

        private async Task<Dictionary<string, DeleteLinkedFileStatus>> EnqueueDeletes(InternalDriveFileId fileId,
            GlobalTransitIdFileIdentifier remoteGlobalTransitIdFileIdentifier,
            FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var results = new Dictionary<string, DeleteLinkedFileStatus>();

            foreach (var r in recipients)
            {
                var recipient = (OdinId)r;

                //TODO: i need to resolve the token outside of transit, pass it in as options instead
                var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, cn);
                var encryptedClientAccessToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes();

                var item = new OutboxFileItem()
                {
                    Recipient = recipient,
                    Priority = 100,
                    Type = OutboxItemType.DeleteRemoteFile,
                    File = fileId,
                    DependencyFileId = default,
                    State = new OutboxItemState
                    {
                        Recipient = null,
                        IsTransientFile = false,
                        TransferInstructionSet = null,
                        OriginalTransitOptions = null,
                        EncryptedClientAuthToken = encryptedClientAccessToken,
                        Data = OdinSystemSerializer.Serialize(new DeleteRemoteFileRequest()
                        {
                            RemoteGlobalTransitIdFileIdentifier = remoteGlobalTransitIdFileIdentifier,
                            FileSystemType = fileTransferOptions.FileSystemType
                        }).ToUtf8ByteArray()
                    }
                };

                await peerOutbox.AddItem(item, cn, useUpsert: true);
                results.Add(recipient.DomainName, DeleteLinkedFileStatus.Enqueued);
            }

            await outboxProcessorAsync.StartOutboxProcessingAsync(odinContext, cn);

            return results;
        }

        private async Task<SendReadReceiptResultRecipientStatusItem> SendReadReceiptToRecipient(ServerFileHeader header,
            InternalDriveFileId fileId, IOdinContext odinContext,
            DatabaseConnection cn, FileSystemType fileSystemType)
        {
            var recipient = (OdinId)header.FileMetadata.SenderOdinId;

            var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, cn, false);
            if (null == clientAuthToken)
            {
                return new SendReadReceiptResultRecipientStatusItem()
                {
                    Recipient = recipient,
                    Status = SendReadReceiptResultStatus.NotConnectedToOriginalSender
                };
            }

            async Task<ApiResponse<PeerTransferResponse>> TrySend()
            {
                var client = _odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient,
                    clientAuthToken.ToAuthenticationToken(), fileSystemType);

                var response = await client.MarkFileAsRead(new MarkFileAsReadRequest()
                {
                    GlobalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier
                    {
                        TargetDrive = odinContext.PermissionsContext.GetTargetDrive(fileId.DriveId),
                        GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault()
                    },
                    FileSystemType = fileSystemType
                });

                return response;
            }

            try
            {
                ApiResponse<PeerTransferResponse> response = null;

                await TryRetry.WithDelayAsync(
                    odinConfiguration.Host.PeerOperationMaxAttempts,
                    odinConfiguration.Host.PeerOperationDelayMs,
                    CancellationToken.None,
                    async () => { response = await TrySend(); });

                {
                    if (response.IsSuccessStatusCode)
                    {
                        return new SendReadReceiptResultRecipientStatusItem
                        {
                            Recipient = recipient,
                            Status = SendReadReceiptResultStatus.RequestAcceptedIntoInbox
                        };
                    }

                    return new SendReadReceiptResultRecipientStatusItem
                    {
                        Recipient = recipient,
                        Status = MapPeerErrorResponseFromHttpStatus(response)
                    };
                }
            }
            catch (TryRetryException ex)
            {
                var e = ex.InnerException;
                logger.LogError(e, "Recipient server not responding");
                return new SendReadReceiptResultRecipientStatusItem
                {
                    Recipient = recipient,
                    Status = SendReadReceiptResultStatus.RecipientIdentityReturnedServerError
                };
            }
        }

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

        private async Task<(Dictionary<string, TransferStatus> transferStatus, IEnumerable<OutboxFileItem>)> CreateOutboxItems(InternalDriveFileId internalFile,
            TransitOptions options,
            FileTransferOptions fileTransferOptions,
            IOdinContext odinContext,
            int priority,
            DatabaseConnection cn)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);
            TargetDrive targetDrive = options.RemoteTargetDrive ?? (await driveManager.GetDrive(internalFile.DriveId, cn, failIfInvalid: true)).TargetDriveInfo;

            var status = new Dictionary<string, TransferStatus>();
            var outboxItems = new List<OutboxFileItem>();

            if (options.Recipients?.Contains(tenantContext.HostOdinId) ?? false)
            {
                throw new OdinClientException("Cannot transfer a file to the sender; what's the point?", OdinClientErrorCode.InvalidRecipient);
            }

            var header = await fs.Storage.GetServerFileHeader(internalFile, odinContext, cn);
            var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(internalFile.DriveId);

            var keyHeader = header.FileMetadata.IsEncrypted ? header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey) : KeyHeader.Empty();
            storageKey.Wipe();

            foreach (var r in options.Recipients!)
            {
                var recipient = (OdinId)r;
                try
                {
                    //TODO: i need to resolve the token outside of transit, pass it in as options instead
                    //TODO: apply encryption before storing in the outbox
                    var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, cn);
                    var encryptedClientAccessToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes();

                    outboxItems.Add(new OutboxFileItem()
                    {
                        Priority = priority,
                        Type = OutboxItemType.File,
                        File = internalFile,
                        Recipient = recipient,
                        DependencyFileId = options.OutboxDependencyFileId,
                        State = new OutboxItemState()
                        {
                            IsTransientFile = options.IsTransient,
                            Attempts = { },
                            OriginalTransitOptions = options,
                            EncryptedClientAuthToken = encryptedClientAccessToken,
                            TransferInstructionSet = CreateTransferInstructionSet(
                                keyHeader,
                                clientAuthToken,
                                targetDrive,
                                fileTransferOptions.TransferFileType,
                                fileTransferOptions.FileSystemType,
                                options),
                            Data = []
                        }
                    });

                    status.Add(recipient, TransferStatus.Enqueued);
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed while creating outbox item {msg}", ex.Message);
                    status.Add(recipient, TransferStatus.EnqueuedFailed);
                }
            }

            return (status, outboxItems);
        }


        private SendReadReceiptResultStatus MapPeerErrorResponseFromHttpStatus(ApiResponse<PeerTransferResponse> response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return SendReadReceiptResultStatus.RecipientIdentityReturnedAccessDenied;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return SendReadReceiptResultStatus.RecipientIdentityReturnedBadRequest;
            }

            return SendReadReceiptResultStatus.RecipientIdentityReturnedServerError;
        }
    }
}