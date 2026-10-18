using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Background;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.FileUpdate;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public class PeerOutgoingTransferService(
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        TenantContext tenantContext,
        CircleNetworkService circleNetworkService,
        IDriveManager driveManager,
        FileSystemResolver fileSystemResolver,
        ILogger<PeerOutgoingTransferService> logger,
        IBackgroundServiceNotifier<PeerOutboxProcessorBackgroundService> backgroundServiceNotifier,
        PushNotificationService pushNotificationService,
        OdinConfiguration odinConfiguration)
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService, fileSystemResolver, odinConfiguration)
    {
        public const int MaxRecipientCount = 1000;

        /// <summary>
        /// The most recipients one file may be sent to, or told to delete
        /// </summary>
        public static void AssertRecipientCount(int count) =>
            OdinValidationUtils.AssertIsTrue(count <= MaxRecipientCount, $"max recipients is {MaxRecipientCount}");
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;

        /// <summary>
        /// Sends the specified file
        /// </summary>
        /// <returns></returns>
        public async Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile,
            TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType,
            IOdinContext odinContext,
            DataSource overrideDataSource = null)
        {
            odinContext.PermissionsContext.AssertCanUsePeer(PeerAccess.Write);

            OdinValidationUtils.AssertValidRecipientList(options.Recipients, allowEmpty: true, tenant: tenantContext.HostOdinId);
            AssertRecipientCount(options.Recipients.Count);

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

            var (outboxStatus, outboxItems) = await CreateOutboxItems(internalFile, options, sfo, odinContext, priority,
                overrideDataSource);

            //TODO: change this to a batch update of the transfer history
            foreach (var item in outboxItems)
            {
                var fs = _fileSystemResolver.ResolveFileSystem(item.State.TransferInstructionSet.FileSystemType);
                await fs.Storage.InitiateTransferHistoryAsync(internalFile, item.Recipient, odinContext);
                await peerOutbox.AddItemAsync(item, useUpsert: true);
            }

            await ProcessOutboxNow();

            return outboxStatus;
        }

        public async Task ProcessOutboxNow()
        {
            logger.LogDebug("Processing outbox now called");
            await backgroundServiceNotifier.NotifyWorkAvailableAsync();
        }

        /// <summary>
        /// Updates a remote file
        /// </summary>
        public async Task<Dictionary<string, TransferStatus>> UpdateFile(InternalDriveFileId sourceFile,
            KeyHeader keyHeader,
            FileIdentifier file,
            UploadManifest manifest,
            List<OdinId> recipients,
            Guid newVersionTag,
            FileSystemType fileSystemType,
            AppNotificationOptions notificationOptions,
            UpdateLocale updateLocale,
            IOdinContext odinContext,
            DataSource overrideDataSource = null)
        {
            odinContext.PermissionsContext.AssertCanUsePeer(PeerAccess.Write);
            OdinValidationUtils.AssertValidRecipientList(recipients, allowEmpty: true, tenant: tenantContext.HostOdinId);
            AssertRecipientCount(recipients.Count);

            var request = new UpdateRemoteFileRequest()
            {
                File = file,
                Manifest = manifest,
                NewVersionTag = newVersionTag,
                AppNotificationOptions = notificationOptions,
                UpdateLocale = updateLocale
            };

            var priority = 100;

            var (outboxStatus, outboxItems) = await CreateUpdateOutboxItemsAsync(
                sourceFile,
                keyHeader,
                request,
                recipients,
                priority,
                fileSystemType,
                updateLocale,
                odinContext,
                overrideDataSource);

            //TODO: change this to a batch update of the transfer history
            foreach (var item in outboxItems)
            {
                if (!item.State.IsTransientFile)
                {
                    var fs = _fileSystemResolver.ResolveFileSystem(item.State.TransferInstructionSet.FileSystemType);
                    await fs.Storage.InitiateTransferHistoryAsync(sourceFile, item.Recipient, odinContext);
                }

                await peerOutbox.AddItemAsync(item, useUpsert: true);
            }

            await backgroundServiceNotifier.NotifyWorkAvailableAsync();

            return outboxStatus;
        }

        public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(
            GlobalTransitIdFileIdentifier remoteGlobalTransitIdFileIdentifier,
            FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients,
            IOdinContext odinContext)
        {
            var fileId = new InternalDriveFileId()
            {
                FileId = remoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                DriveId = SystemDriveConstants.TransientTempDrive.Alias
            };

            var result = await EnqueueDeletes(fileId, remoteGlobalTransitIdFileIdentifier, fileTransferOptions,
                recipients.ToOdinIdList(), async r => (await ResolveClientAccessTokenAsync(r, odinContext)).ToAuthenticationToken().ToPortableBytes());

            return result;
        }

        public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(
            InternalDriveFileId fileId,
            FileTransferOptions fileTransferOptions,
            IEnumerable<string> recipients,
            IOdinContext odinContext)
        {
            var recipientList = recipients?.ToList() ?? [];
            logger.LogDebug("[DeleteFlow] SendDeleteFileRequest -> tenant:{tenant} fileId:{fileId} fileSystemType:{fst} recipientCount:{rc}",
                odinContext.Tenant, fileId, fileTransferOptions.FileSystemType, recipientList.Count);

            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);
            var header = await fs.Storage.GetServerFileHeader(fileId, odinContext);

            if (null == header)
            {
                logger.LogWarning("[DeleteFlow] SendDeleteFileRequest -> file not found fileId:{fileId} fileSystemType:{fst}",
                    fileId, fileTransferOptions.FileSystemType);
                throw new OdinClientException("File not found", OdinClientErrorCode.InvalidFile);
            }

            return await EnqueueDeletes(header, fileTransferOptions, recipientList.ToOdinIdList(),
                r => ResolveTokenOrThrowAsync(r, odinContext), odinContext);
        }

        /// <summary>
        /// Each distinct recipient's encrypted access token, null for one the caller is no longer connected to. Resolved
        /// once for a whole batch, so a refusal (no transit permission, no ICR key) comes before anything is sent.
        /// </summary>
        public async Task<Dictionary<OdinId, byte[]>> ResolveRecipientTokensAsync(IEnumerable<OdinId> recipients,
            IOdinContext odinContext)
        {
            var tokens = new Dictionary<OdinId, byte[]>();
            foreach (var recipient in recipients.Distinct())
            {
                var token = await ResolveClientAccessTokenAsync(recipient, odinContext, PeerAccess.Write, failIfNotConnected: false);
                tokens[recipient] = token?.ToAuthenticationToken().ToPortableBytes();
            }

            return tokens;
        }

        /// <summary>
        /// Tells <paramref name="recipients"/> to delete their copies of the file <paramref name="header"/> describes, which
        /// the caller has already read, with the tokens <see cref="ResolveRecipientTokensAsync"/> resolved. A recipient
        /// without a token (no longer connected, or not resolved) is EnqueueFailed, and so is every recipient of a file
        /// never sent to anyone (no global transit id for them to match).
        /// </summary>
        public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(ServerFileHeader header,
            IEnumerable<OdinId> recipients, IReadOnlyDictionary<OdinId, byte[]> recipientTokens, IOdinContext odinContext)
        {
            var list = recipients?.ToList() ?? [];
            if (header.FileMetadata.GlobalTransitId == null)
            {
                return list.ToDictionary(r => r.DomainName, _ => DeleteLinkedFileStatus.EnqueueFailed);
            }

            var options = new FileTransferOptions
            {
                FileSystemType = header.ServerMetadata.FileSystemType,
                TransferFileType = TransferFileType.Normal
            };
            return await EnqueueDeletes(header, options, list,
                r => Task.FromResult(recipientTokens.GetValueOrDefault(r)), odinContext);
        }

        /// <summary>
        /// A drive file delete by file id: an ordinary file operation, so it needs UseTransitWrite
        /// </summary>
        private async Task<byte[]> ResolveTokenOrThrowAsync(OdinId recipient, IOdinContext odinContext)
        {
            var token = await ResolveClientAccessTokenAsync(recipient, odinContext, PeerAccess.Write);
            return token.ToAuthenticationToken().ToPortableBytes();
        }

        private async Task<Dictionary<string, DeleteLinkedFileStatus>> EnqueueDeletes(ServerFileHeader header,
            FileTransferOptions fileTransferOptions, IEnumerable<OdinId> recipients, Func<OdinId, Task<byte[]>> tokenFor,
            IOdinContext odinContext)
        {
            var fileId = header.FileMetadata.File;
            var remoteGlobalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = odinContext.PermissionsContext.GetTargetDrive(fileId.DriveId)
            };

            logger.LogDebug("[DeleteFlow] SendDeleteFileRequest -> fileId: {fileId} -> resolved gtid:{gtid} targetDrive:{drive}; calling EnqueueDeletes",
                fileId,
                remoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                remoteGlobalTransitIdFileIdentifier.TargetDrive);

            return await EnqueueDeletes(fileId, remoteGlobalTransitIdFileIdentifier, fileTransferOptions, recipients, tokenFor);
        }

        /// <summary>
        /// Sends a notification to the original sender indicating the file was read for all files created before asOfTimestamp
        /// </summary>
        public async Task<SendReadReceiptResult> SendReadReceipt(Guid driveId, FileQueryParams queryParams, UnixTimeUtc upToTimestamp,
            IOdinContext odinContext,
            FileSystemType fileSystemType, UnixTimeUtc? timestamp = null)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileSystemType);

            var qp = new FileQueryParams
            {
                FileType = queryParams?.FileType,
                DataType = queryParams?.DataType,
                ArchivalStatus = queryParams?.ArchivalStatus,
                Sender = queryParams?.Sender,
                GroupId = queryParams?.GroupId,
                UserDate = queryParams?.UserDate,
                ClientUniqueIdAtLeastOne = queryParams?.ClientUniqueIdAtLeastOne,
                TagsMatchAtLeastOne = queryParams?.TagsMatchAtLeastOne,
                TagsMatchAll = queryParams?.TagsMatchAll,
                LocalTagsMatchAtLeastOne = queryParams?.LocalTagsMatchAtLeastOne,
                LocalTagsMatchAll = queryParams?.LocalTagsMatchAll,
                GlobalTransitId = queryParams?.GlobalTransitId,


                FileState = [FileState.Active]
            };

            var options = new QueryBatchResultOptions
            {
                MaxRecords = 1000,
                IncludeHeaderContent = false,
                ExcludePreviewThumbnail = true,
                ExcludeServerMetaData = true,
                IncludeTransferHistory = false,
                Cursor = new QueryBatchCursor(upToTimestamp), // files up to this point
                Ordering = QueryBatchSortOrder.OldestFirst,
                Sorting = QueryBatchSortField.CreatedDate
            };

            var results = await fs.Query.GetBatch(driveId, qp, options, odinContext);

            var files = results.SearchResults
                .Where(f => f.FileMetadata.LocalAppData?.ReadTime == null // not already set
                            && !f.IsOriginalAuthor(odinContext.Caller.OdinId.GetValueOrDefault()))
                .Select(f => new InternalDriveFileId(driveId, f.FileId))
                .ToList();

            return await SendReadReceipt(files, odinContext, fileSystemType, timestamp);
        }

        /// <summary>
        /// Sends a notification to the original sender indicating the file was read
        /// </summary>
        public async Task<SendReadReceiptResult> SendReadReceipt(List<InternalDriveFileId> files, IOdinContext odinContext,
            FileSystemType fileSystemType, UnixTimeUtc? timestamp = null)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileSystemType);
            foreach (var file in files)
            {
                await fs.Storage.AssertCanReadOrWriteToDriveAsync(file.DriveId, odinContext);
            }

            // This is all ugly mapping code but 🤷
            var intermediateResults = new List<(ExternalFileIdentifier File, SendReadReceiptResultRecipientStatusItem StatusItem)>();
            foreach (var fileId in files)
            {
                var externalFile = new ExternalFileIdentifier()
                {
                    FileId = fileId.FileId,
                    TargetDrive = odinContext.PermissionsContext.GetTargetDrive(fileId.DriveId)
                };

                var statusItem = await EnqueueReadReceiptAsync(fileId, odinContext, fileSystemType);
                intermediateResults.Add((externalFile, statusItem));
            }

            await backgroundServiceNotifier.NotifyWorkAvailableAsync();

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

            var successfulFiles = new List<InternalDriveFileId>();
            foreach (var file in files)
            {
                // Update localappdata for this file now that we've enqueued everything
                try
                {
                    var success = await fs.Storage.UpdateLocalReadTime(file, odinContext, timestamp);
                    if (success)
                    {
                        successfulFiles.Add(file);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to update local read time for file: {file}", file);
                }
            }

            return new SendReadReceiptResult()
            {
                Results = results
            };
        }


        /// <summary>
        /// Tells a peer identity to send a push notification to its owner
        /// </summary>
        public async Task SendPeerPushNotification(AppNotificationOptions options, Guid driveId, IOdinContext odinContext)
        {
            OdinValidationUtils.AssertValidRecipientList(options.Recipients, false);
            AssertRecipientCount(options.Recipients.Count);

            //ISSUE: this is running as the identity uploading the file, which cannot read the ICR key to decrypt the CAT
            // var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, odinContext, false);
            // if (null == clientAuthToken)
            // {
            //     logger.LogDebug("Attempt to distribute to recipient ({r}) who is not connected", recipient);
            //     return;
            // }

            foreach (var recipient in options.Recipients.Without(odinContext.Tenant))
            {
                try
                {
                    var item = new OutboxFileItem
                    {
                        Recipient = recipient,
                        Priority = 100, //super high priority to ensure these are sent quickly,
                        Type = OutboxItemType.PeerPushNotification,
                        AttemptCount = 0,
                        File = new InternalDriveFileId()
                        {
                            DriveId = driveId,
                            FileId = SequentialGuid.CreateGuid()
                        },
                        DependencyFileId = default,
                        State = new OutboxItemState
                        {
                            TransferInstructionSet = null,
                            OriginalTransitOptions = null,
                            // EncryptedClientAuthToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes(),
                            Data = OdinSystemSerializer.Serialize(new PushNotificationOutboxRecord()
                                {
                                    SenderId = odinContext.GetCallerOdinIdOrFail(),
                                    Options = options,
                                    Timestamp = UnixTimeUtc.Now()
                                        .milliseconds
                                })
                                .ToUtf8ByteArray()
                        },
                    };

                    await peerOutbox.AddItemAsync(item, useUpsert: true);
                }
                catch (Exception e)
                {
                    logger.LogInformation(e, "Failed why enqueueing peer push notification for recipient ({r})", recipient);
                }
            }

            if (options.Recipients.Any(r => r == odinContext.Tenant))
            {
                var senderId = odinContext.GetCallerOdinIdOrFail();
                var newContext = OdinContextUpgrades.UpgradeToPeerTransferContext(odinContext);
                await pushNotificationService.EnqueueNotification(senderId, options, newContext);
            }
        }

        // 

        private async Task<SendReadReceiptResultRecipientStatusItem> EnqueueReadReceiptAsync(InternalDriveFileId fileId,
            IOdinContext odinContext,
            FileSystemType fileSystemType)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileSystemType);
            var header = await fs.Storage.GetServerFileHeader(fileId, odinContext);

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

            var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, odinContext, false);
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

            await peerOutbox.AddItemAsync(outboxItem, useUpsert: true);

            logger.LogDebug("Enqueued Read-receipt for GTID: {g}", header.FileMetadata.GlobalTransitId);
            return new SendReadReceiptResultRecipientStatusItem()
            {
                Recipient = recipient,
                Status = SendReadReceiptResultStatus.Enqueued
            };
        }

        private async Task<Dictionary<string, DeleteLinkedFileStatus>> EnqueueDeletes(InternalDriveFileId fileId,
            GlobalTransitIdFileIdentifier remoteGlobalTransitIdFileIdentifier,
            FileTransferOptions fileTransferOptions,
            IEnumerable<OdinId> recipients,
            Func<OdinId, Task<byte[]>> tokenFor)
        {
            var results = new Dictionary<string, DeleteLinkedFileStatus>();

            foreach (var recipient in recipients)
            {
                var encryptedClientAccessToken = await tokenFor(recipient);
                if (encryptedClientAccessToken == null)
                {
                    results.Add(recipient.DomainName, DeleteLinkedFileStatus.EnqueueFailed);
                    continue;
                }

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

                await peerOutbox.AddItemAsync(item, useUpsert: true);
                results.Add(recipient.DomainName, DeleteLinkedFileStatus.Enqueued);

                logger.LogDebug("[DeleteFlow] EnqueueDeletes -> outbox item added recipient:{recipient} gtid:{gtid} fileId:{fileId} fileSystemType:{fst}",
                    recipient, remoteGlobalTransitIdFileIdentifier.GlobalTransitId, fileId, fileTransferOptions.FileSystemType);
            }

            logger.LogDebug("[DeleteFlow] EnqueueDeletes -> notifying outbox processor of {count} new delete item(s) for gtid:{gtid}",
                results.Count, remoteGlobalTransitIdFileIdentifier.GlobalTransitId);

            await backgroundServiceNotifier.NotifyWorkAvailableAsync();

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
                SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
            };
        }

        private async Task<(Dictionary<string, TransferStatus> transferStatus, IEnumerable<OutboxFileItem>)> CreateOutboxItems(
            InternalDriveFileId internalFile,
            TransitOptions options,
            FileTransferOptions fileTransferOptions,
            IOdinContext odinContext,
            int priority,
            DataSource overrideDataSource)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);
            TargetDrive targetDrive = options.RemoteTargetDrive ??
                                      (await driveManager.GetDriveAsync(internalFile.DriveId, failIfInvalid: true)).TargetDriveInfo;

            var status = new Dictionary<string, TransferStatus>();
            var outboxItems = new List<OutboxFileItem>();

            if (options.Recipients?.Contains(tenantContext.HostOdinId) ?? false)
            {
                throw new OdinClientException("Cannot transfer a file to the sender; what's the point?",
                    OdinClientErrorCode.InvalidRecipient);
            }

            var header = await fs.Storage.GetServerFileHeader(internalFile, odinContext);
            var storageKey = odinContext.PermissionsContext.GetDriveStorageKey(internalFile.DriveId);

            var keyHeader = header.FileMetadata.IsEncrypted
                ? header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey)
                : KeyHeader.Empty();
            storageKey.Wipe();

            foreach (var r in options.Recipients!)
            {
                var recipient = (OdinId)r;
                try
                {
                    //TODO: i need to resolve the token outside of transit, pass it in as options instead
                    //TODO: apply encryption before storing in the outbox
                    var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, odinContext, PeerAccess.Write);
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
                            Data = [],
                            DataSourceOverride = overrideDataSource
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

        private async Task<(Dictionary<string, TransferStatus> transferStatus, IEnumerable<OutboxFileItem> outboxItems)>
            CreateUpdateOutboxItemsAsync(InternalDriveFileId sourceFile,
                KeyHeader keyHeader,
                UpdateRemoteFileRequest request,
                List<OdinId> recipients,
                int priority,
                FileSystemType fileSystemType,
                UpdateLocale updateLocale,
                IOdinContext odinContext, DataSource overrideDataSource)
        {
            var status = new Dictionary<string, TransferStatus>();
            var outboxItems = new List<OutboxFileItem>();

            foreach (var recipient in recipients)
            {
                try
                {
                    var clientAuthToken = await ResolveClientAccessTokenAsync(recipient, odinContext, PeerAccess.Write);
                    var encryptedClientAccessToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes();

                    var iv = ByteArrayUtil.GetRndByteArray(16);
                    var ss = clientAuthToken.SharedSecret;

                    var updateInstructionSet = new EncryptedRecipientFileUpdateInstructionSet()
                    {
                        FileSystemType = fileSystemType,
                        EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, iv, ref ss),
                        Request = request
                    };


                    outboxItems.Add(new OutboxFileItem()
                    {
                        Type = OutboxItemType.RemoteFileUpdate,
                        File = sourceFile,
                        Priority = priority,
                        Recipient = recipient,
                        State = new OutboxItemState
                        {
                            Recipient = null,
                            IsTransientFile = updateLocale == UpdateLocale.Peer,
                            EncryptedClientAuthToken = encryptedClientAccessToken,
                            TransferInstructionSet = new EncryptedRecipientTransferInstructionSet()
                            {
                                FileSystemType = fileSystemType
                            },
                            OriginalTransitOptions = null,
                            DataSourceOverride = overrideDataSource,
                            Data = OdinSystemSerializer.Serialize(updateInstructionSet).ToUtf8ByteArray()
                        }
                    });

                    status.Add(recipient, TransferStatus.Enqueued);
                }
                catch (Exception ex)
                {
                    logger.LogInformation("Failed while creating outbox item {msg}", ex.Message);
                    status.Add(recipient, TransferStatus.EnqueuedFailed);
                }
            }

            return (status, outboxItems);
        }
    }
}