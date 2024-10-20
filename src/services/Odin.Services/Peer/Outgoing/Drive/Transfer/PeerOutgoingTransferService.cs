using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public class PeerOutgoingTransferService(
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        TenantContext tenantContext,
        CircleNetworkService circleNetworkService,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver,
        ILogger<PeerOutgoingTransferService> logger,
        PeerOutboxProcessorBackgroundService outboxProcessorBackgroundService
    )
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver), IPeerOutgoingTransferService
    {
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;

        public async Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile,
            TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType, IOdinContext odinContext,
            IdentityDatabase db)
        {
            odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitWrite);

            OdinValidationUtils.AssertValidRecipientList(options.Recipients, allowEmpty: true, tenant: tenantContext.HostOdinId);

            var sfo = new FileTransferOptions()
            {
                TransferFileType = transferFileType,
                FileSystemType = fileSystemType
            };

            var priority = options.Priority switch
            {
                OutboxPriority.High => 1000,
                OutboxPriority.Medium => 2000,
                _ => 3000
            };

            var (outboxStatus, outboxItems) = await CreateOutboxItems(internalFile, options, sfo, odinContext, priority, db);

            //TODO: change this to a batch update of the transfer history
            foreach (var item in outboxItems)
            {
                var fs = _fileSystemResolver.ResolveFileSystem(item.State.TransferInstructionSet.FileSystemType);
                await fs.Storage.UpdateTransferHistory(internalFile, item.Recipient, new UpdateTransferHistoryData() { IsInOutbox = true }, odinContext, db);
                await peerOutbox.AddItem(item, db);
            }

            outboxProcessorBackgroundService.PulseBackgroundProcessor();

            return outboxStatus;
        }

        public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(
            GlobalTransitIdFileIdentifier remoteGlobalTransitIdFileIdentifier,
            FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients,
            IOdinContext odinContext,
            IdentityDatabase db)
        {
            var fileId = new InternalDriveFileId()
            {
                FileId = remoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                DriveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.TransientTempDrive)
            };

            var result = await EnqueueDeletes(fileId, remoteGlobalTransitIdFileIdentifier, fileTransferOptions, recipients, odinContext, db);

            return result;
        }

        public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(
            InternalDriveFileId fileId,
            FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients,
            IOdinContext odinContext,
            IdentityDatabase db)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);
            var header = await fs.Storage.GetServerFileHeader(fileId, odinContext, db);

            if (null == header)
            {
                throw new OdinClientException("File not found", OdinClientErrorCode.InvalidFile);
            }

            var remoteGlobalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = odinContext.PermissionsContext.GetTargetDrive(header.FileMetadata.File.DriveId)
            };

            return await EnqueueDeletes(fileId, remoteGlobalTransitIdFileIdentifier, fileTransferOptions, recipients, odinContext, db);
        }

        public async Task<SendReadReceiptResult> SendReadReceipt(List<InternalDriveFileId> files, IOdinContext odinContext,
            IdentityDatabase db,
            FileSystemType fileSystemType)
        {
            // This is all ugly mapping code but 🤷
            var intermediateResults = new List<(ExternalFileIdentifier File, SendReadReceiptResultRecipientStatusItem StatusItem)>();
            foreach (var fileId in files)
            {
                var externalFile = new ExternalFileIdentifier()
                {
                    FileId = fileId.FileId,
                    TargetDrive = odinContext.PermissionsContext.GetTargetDrive(fileId.DriveId)
                };

                var statusItem = await EnqueueReadReceipt(fileId, odinContext, db, fileSystemType);
                intermediateResults.Add((externalFile, statusItem));
            }

            outboxProcessorBackgroundService.PulseBackgroundProcessor();

            // This, too, is all ugly mapping code but 🤷
            var results = new List<SendReadReceiptResultFileItem>();
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

        private async Task<SendReadReceiptResultRecipientStatusItem> EnqueueReadReceipt(InternalDriveFileId fileId,
            IOdinContext odinContext,
            IdentityDatabase db,
            FileSystemType fileSystemType)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileSystemType);
            var header = await fs.Storage.GetServerFileHeader(fileId, odinContext, db);

            if (header == null)
            {
                return new SendReadReceiptResultRecipientStatusItem()
                {
                    Recipient = null,
                    Status = SendReadReceiptResultStatus.FileDoesNotExist
                };
            }

            if (string.IsNullOrEmpty(header.FileMetadata.SenderOdinId?.Trim()))
            {
                return new SendReadReceiptResultRecipientStatusItem()
                {
                    Recipient = null,
                    Status = SendReadReceiptResultStatus.FileDoesNotHaveSender
                };
            }

            var recipient = (OdinId)header.FileMetadata.SenderOdinId;

            if (recipient == odinContext.Tenant)
            {
                return new SendReadReceiptResultRecipientStatusItem()
                {
                    Recipient = null,
                    Status = SendReadReceiptResultStatus.CannotSendReadReceiptToSelf
                };
            }


            if (header.FileMetadata.GlobalTransitId == null)
            {
                return new SendReadReceiptResultRecipientStatusItem()
                {
                    Recipient = recipient,
                    Status = SendReadReceiptResultStatus.MissingGlobalTransitId
                };
            }

            var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, db, false);
            if (null == clientAuthToken)
            {
                return new SendReadReceiptResultRecipientStatusItem()
                {
                    Recipient = recipient,
                    Status = SendReadReceiptResultStatus.NotConnectedToOriginalSender
                };
            }

            var request = new MarkFileAsReadRequest()
            {
                GlobalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier
                {
                    TargetDrive = odinContext.PermissionsContext.GetTargetDrive(fileId.DriveId),
                    GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault()
                },
                FileSystemType = fileSystemType
            };

            var outboxItem = new OutboxFileItem
            {
                Recipient = recipient,
                File = header.FileMetadata.File,
                Priority = 100,
                Type = OutboxItemType.ReadReceipt,
                State = new OutboxItemState
                {
                    Recipient = null,
                    IsTransientFile = false,
                    TransferInstructionSet = null,
                    OriginalTransitOptions = null,
                    EncryptedClientAuthToken = clientAuthToken.ToPortableBytes(),
                    Data = OdinSystemSerializer.Serialize(request).ToUtf8ByteArray()
                }
            };

            await peerOutbox.AddItem(outboxItem, db, useUpsert: true);

            return new SendReadReceiptResultRecipientStatusItem()
            {
                Recipient = recipient,
                Status = SendReadReceiptResultStatus.Enqueued
            };
        }

        private async Task<Dictionary<string, DeleteLinkedFileStatus>> EnqueueDeletes(InternalDriveFileId fileId,
            GlobalTransitIdFileIdentifier remoteGlobalTransitIdFileIdentifier,
            FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients,
            IOdinContext odinContext,
            IdentityDatabase db)
        {
            var results = new Dictionary<string, DeleteLinkedFileStatus>();

            foreach (var r in recipients)
            {
                var recipient = (OdinId)r;

                //TODO: i need to resolve the token outside of transit, pass it in as options instead
                var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, db);
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

                await peerOutbox.AddItem(item, db, useUpsert: true);
                results.Add(recipient.DomainName, DeleteLinkedFileStatus.Enqueued);
            }

            outboxProcessorBackgroundService.PulseBackgroundProcessor();

            return results;
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
            IdentityDatabase db)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);
            TargetDrive targetDrive = options.RemoteTargetDrive ?? (await driveManager.GetDrive(internalFile.DriveId, db, failIfInvalid: true)).TargetDriveInfo;

            var status = new Dictionary<string, TransferStatus>();
            var outboxItems = new List<OutboxFileItem>();

            if (options.Recipients?.Contains(tenantContext.HostOdinId) ?? false)
            {
                throw new OdinClientException("Cannot transfer a file to the sender; what's the point?", OdinClientErrorCode.InvalidRecipient);
            }

            var header = await fs.Storage.GetServerFileHeader(internalFile, odinContext, db);
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
                    var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, db);
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
    }
}