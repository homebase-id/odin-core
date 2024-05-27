using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public class PeerOutgoingTransferService(
        IPeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        TenantContext tenantContext,
        CircleNetworkService circleNetworkService,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration,
        ServerSystemStorage serverSystemStorage,
        ILogger<PeerOutgoingTransferService> logger,
        PeerOutboxProcessor outboxProcessor)
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService,
            fileSystemResolver), IPeerOutgoingTransferService
    {
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory = odinHttpClientFactory;

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

            int priority = 100;
            // var priority = options.Priority switch
            // {
            //     PriorityOptions.High => 1000,
            //     PriorityOptions.Medium => 2000,
            //     _ => 3000
            // };

            if (options.Schedule == ScheduleOptions.SendNowAwaitResponse)
            {
                //send now
                return await SendFileNow(internalFile, options, sfo, odinContext, cn);
            }

            return await SendFileLater(internalFile, options, sfo, priority, odinContext, cn);
        }

        public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(GlobalTransitIdFileIdentifier remoteGlobalTransitIdentifier,
            FileTransferOptions fileTransferOptions, IEnumerable<string> recipients, IOdinContext odinContext, DatabaseConnection cn)
        {
            var result = new Dictionary<string, DeleteLinkedFileStatus>();

            foreach (var recipient in recipients)
            {
                var r = (OdinId)recipient;

                var clientAccessToken = await ResolveClientAccessToken(r, odinContext, cn);

                var client = _odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(r, clientAccessToken.ToAuthenticationToken(),
                    fileSystemType: fileTransferOptions.FileSystemType);

                ApiResponse<PeerTransferResponse> httpResponse = null;

                await TryRetry.WithDelayAsync(
                    odinConfiguration.Host.PeerOperationMaxAttempts,
                    odinConfiguration.Host.PeerOperationDelayMs,
                    CancellationToken.None,
                    async () =>
                    {
                        httpResponse = await client.DeleteLinkedFile(new DeleteRemoteFileRequest()
                        {
                            RemoteGlobalTransitIdFileIdentifier = remoteGlobalTransitIdentifier,
                            FileSystemType = fileTransferOptions.FileSystemType
                        });
                    });

                if (httpResponse.IsSuccessStatusCode)
                {
                    var transitResponse = httpResponse.Content;
                    switch (transitResponse.Code)
                    {
                        case PeerResponseCode.AcceptedIntoInbox:
                        case PeerResponseCode.AcceptedDirectWrite:
                            result.Add(recipient, DeleteLinkedFileStatus.RequestAccepted);
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else
                {
                    result.Add(recipient, DeleteLinkedFileStatus.RemoteServerFailed);
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

        private async Task<(Dictionary<string, bool> transferStatus, IEnumerable<OutboxItem>)> CreateOutboxItems(InternalDriveFileId internalFile,
            TransitOptions options,
            FileTransferOptions fileTransferOptions,
            IOdinContext odinContext,
            int priority,
            DatabaseConnection cn)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);

            TargetDrive targetDrive = options.RemoteTargetDrive ?? (await driveManager.GetDrive(internalFile.DriveId, cn, failIfInvalid: true)).TargetDriveInfo;

            var status = new Dictionary<string, bool>();
            var outboxItems = new List<OutboxItem>();

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
                    var clientAuthToken = await ResolveClientAccessToken(recipient, odinContext, cn);

                    //TODO: apply encryption before storing in the outbox
                    var encryptedClientAccessToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes();

                    outboxItems.Add(new OutboxItem()
                    {
                        Marker = default, //marker is added when actually store the item
                        Priority = priority,
                        Type = OutboxItemType.File,
                        IsTransientFile = options.IsTransient,
                        File = internalFile,
                        Recipient = recipient,
                        OriginalTransitOptions = options,
                        EncryptedClientAuthToken = encryptedClientAccessToken,
                        TransferInstructionSet = CreateTransferInstructionSet(
                            keyHeader,
                            clientAuthToken,
                            targetDrive,
                            fileTransferOptions.TransferFileType,
                            fileTransferOptions.FileSystemType,
                            options)
                    });

                    status.Add(recipient, true);
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed while creating outbox item {msg}", ex.Message);
                    status.Add(recipient, false);
                }
            }

            return (status, outboxItems);
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileLater(InternalDriveFileId internalFile,
            TransitOptions options, FileTransferOptions fileTransferOptions, int priority, IOdinContext odinContext, DatabaseConnection cn)
        {
            var (outboxStatus, outboxItems) = await CreateOutboxItems(internalFile, options, fileTransferOptions, odinContext, priority, cn);
            await peerOutbox.Add(outboxItems, cn);

            _ = outboxProcessor.StartOutboxProcessingAsync(odinContext, cn);

            return await MapOutboxCreationResult(outboxStatus);
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileNow(InternalDriveFileId internalFile,
            TransitOptions transitOptions, FileTransferOptions fileTransferOptions, IOdinContext odinContext, DatabaseConnection cn)
        {
            const int priority = 0;
            var (outboxCreationStatus, outboxItems) = await CreateOutboxItems(internalFile, transitOptions, fileTransferOptions, odinContext, priority, cn);

            //first map the outbox creation status for any that might have failed
            var transferStatus = await MapOutboxCreationResult(outboxCreationStatus);

            var sendResults = await outboxProcessor.ProcessItemsSync(outboxItems, odinContext, cn);

            foreach (var result in sendResults)
            {
                if (result.TransferResult == TransferResult.Success)
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
                            throw new OdinSystemException("Unhandled success scenario in peer transfer");
                    }
                }
                else
                {
                    // Map to something to tell the client
                    switch (result.TransferResult)
                    {
                        case TransferResult.RecipientServerError:
                        case TransferResult.RecipientServerNotResponding:
                        case TransferResult.UnknownError:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.TotalRejectionClientShouldRetry;
                            break;

                        case TransferResult.RecipientDoesNotHavePermissionToFileAcl:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.RecipientDoesNotHavePermissionToFileAcl;
                            break;

                        case TransferResult.FileDoesNotAllowDistribution:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.FileDoesNotAllowDistribution;
                            break;

                        case TransferResult.RecipientServerReturnedAccessDenied:
                            transferStatus[result.Recipient.DomainName] = TransferStatus.RecipientReturnedAccessDenied;
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            return transferStatus;
        }

        private Task<Dictionary<string, TransferStatus>> MapOutboxCreationResult(Dictionary<string, bool> outboxStatus)
        {
            var transferStatus = new Dictionary<string, TransferStatus>();

            foreach (var s in outboxStatus)
            {
                transferStatus.Add(s.Key, s.Value ? TransferStatus.Enqueued : TransferStatus.EnqueuedFailed);
            }

            return Task.FromResult(transferStatus);
        }
    }
}