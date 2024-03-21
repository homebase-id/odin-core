using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.JobManagement;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Peer.Outgoing.Jobs;
using Odin.Services.Util;
using Quartz;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    public class PeerOutgoingOutgoingTransferService(
        OdinContextAccessor contextAccessor,
        PeerOutbox peerOutbox,
        TenantSystemStorage tenantSystemStorage,
        IOdinHttpClientFactory odinHttpClientFactory,
        TenantContext tenantContext,
        CircleNetworkService circleNetworkService,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver,
        OdinConfiguration odinConfiguration,
        JobManager jobManager,
        ILogger<PeerOutgoingOutgoingTransferService> logger)
        : PeerServiceBase(odinHttpClientFactory, circleNetworkService,
            contextAccessor, fileSystemResolver), IPeerOutgoingTransferService
    {
        private readonly FileSystemResolver _fileSystemResolver = fileSystemResolver;
        private readonly TransferKeyEncryptionQueueService _transferKeyEncryptionQueueService = new(tenantSystemStorage);
        private readonly OdinContextAccessor _contextAccessor = contextAccessor;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory = odinHttpClientFactory;

        public async Task ProcessOutbox()
        {
            var batchSize = odinConfiguration.Transit.OutboxBatchSize;

            //Note: here we can prioritize outbox processing by drive if need be
            var drives = await driveManager.GetDrives(PageOptions.All);

            //TODO: prioritize by drive using job manager schedulePriority

            foreach (var drive in drives.Results)
            {
                var batch = await peerOutbox.GetBatchForProcessing(drive.Id, batchSize);
                var schedulePriority = SchedulerGroup.Default;

                var jobKeys = new List<JobKey>();

                //Schedule one job per outbox item
                foreach (var item in batch)
                {
                    var jobKey = await CreateJob(item, schedulePriority);
                    jobKeys.Add(jobKey);
                    //TODO: could store the jobKey in the outbox item so we know what job is running it
                }
            }
        }

        public async Task<Dictionary<string, TransferStatus>> SendFile(InternalDriveFileId internalFile,
            TransitOptions options, TransferFileType transferFileType, FileSystemType fileSystemType)
        {
            _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitWrite);

            OdinValidationUtils.AssertIsTrue(options.Recipients.TrueForAll(r => r != tenantContext.HostOdinId), "You cannot send a file to yourself");
            OdinValidationUtils.AssertValidRecipientList(options.Recipients);

            var sfo = new FileTransferOptions()
            {
                TransferFileType = transferFileType,
                FileSystemType = fileSystemType
            };

            if (options.Schedule == ScheduleOptions.SendNowAwaitResponse)
            {
                //send now
                return await SendFileNow(internalFile, options, sfo);
            }

            return await SendFileLater(internalFile, options, sfo);
        }

        public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(GlobalTransitIdFileIdentifier remoteGlobalTransitIdentifier,
            FileTransferOptions fileTransferOptions, IEnumerable<string> recipients)
        {
            var result = new Dictionary<string, DeleteLinkedFileStatus>();

            foreach (var recipient in recipients)
            {
                var r = (OdinId)recipient;

                var clientAccessToken = await ResolveClientAccessToken(r);

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

        private async Task<JobKey> CreateJob(TransitOutboxItem item, SchedulerGroup schedulePriority)
        {
            var jobKey = await jobManager.Schedule<OutboxItemProcessorJob>(
                new OutboxProcessingJob(_contextAccessor.GetCurrent().Tenant,
                    item,
                    odinConfiguration,
                    schedulePriority));

            return jobKey;
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

        private async Task<(Dictionary<string, bool> transferStatus, IEnumerable<TransitOutboxItem>)> CreateOutboxItems(
            InternalDriveFileId internalFile,
            TransitOptions options,
            FileTransferOptions fileTransferOptions)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(fileTransferOptions.FileSystemType);

            TargetDrive targetDrive = options.RemoteTargetDrive ?? (await driveManager.GetDrive(internalFile.DriveId, failIfInvalid: true)).TargetDriveInfo;

            var status = new Dictionary<string, bool>();
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

                    //TODO: apply encryption before storing in the outbox
                    var encryptedClientAccessToken = clientAuthToken.ToAuthenticationToken().ToPortableBytes();

                    outboxItems.Add(new TransitOutboxItem()
                    {
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
                    AddToTransferKeyEncryptionQueue(recipient, internalFile);
                    status.Add(recipient, false);
                }
            }

            return (status, outboxItems);
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileLater(InternalDriveFileId internalFile,
            TransitOptions options, FileTransferOptions fileTransferOptions)
        {
            //Since the owner is online (in this request) we can prepare a transfer key.  the outbox processor
            //will read the transfer key during the background send process

            var (outboxStatus, outboxItems) = await CreateOutboxItems(internalFile, options, fileTransferOptions);
            await peerOutbox.Add(outboxItems);

            return await MapOutboxCreationResult(outboxStatus);
        }

        private async Task<Dictionary<string, TransferStatus>> SendFileNow(InternalDriveFileId internalFile,
            TransitOptions transitOptions, FileTransferOptions fileTransferOptions)
        {
            var (outboxCreationStatus, outboxItems) = await CreateOutboxItems(internalFile, transitOptions, fileTransferOptions);

            // First map the outbox creation status for any that might have failed
            var transferStatus = await MapOutboxCreationResult(outboxCreationStatus);

            var tasks = outboxItems.Select(async item => await CreateJob(item, SchedulerGroup.FastHighPriority)).ToList();
            var jobKeyList = (await tasks.WhenAll()).ToList();

            //wait for the job results
            var results = new List<OutboxProcessingResult>();
            var completedKeys = new List<JobKey>();
            while (true)
            {
                foreach (var key in jobKeyList)
                {
                    // Scan the list
                    var (response, processingResult) = await jobManager.GetResponse<OutboxProcessingResult>(key);
                    if (response.Status is JobStatus.Completed or JobStatus.Failed)
                    {
                        results.Add(processingResult);
                        completedKeys.Add(key);
                    }

                    //TODO decide what to do with other status codes
                }

                jobKeyList = jobKeyList.Except(completedKeys).ToList();

                if (!jobKeyList.Any())
                {
                    break;
                }

                //todo: consider timeout?
                // For context: this method sends the files instantly while the calling-app waits for
                // the response from ALL recipients (SendNowAwaitResponse).  the prime use-case here is Chat

                // In this case, there could be a long file transfer that could be active but taking
                // a long time  (i.e. multi-gigs) so we need to consider one of the options

                // 1. allow it to play thru and yet i suspect the calling http request will timeout unless we send chunks back
                // 2. block this method (SendNowAwaitResponse) when the file size is over the common image or video size?
                // 3. add a timeout?
            }

            // ... finalize by updated transferStatus
            
            //
            // foreach (var result in results)
            // {
            //     if (result.TransferResult == TransferResult.Success)
            //     {
            //         switch (result.RecipientPeerResponseCode)
            //         {
            //             case PeerResponseCode.AcceptedIntoInbox:
            //                 transferStatus[result.Recipient.DomainName] = TransferStatus.DeliveredToInbox;
            //                 break;
            //
            //             case PeerResponseCode.AcceptedDirectWrite:
            //                 transferStatus[result.Recipient.DomainName] = TransferStatus.DeliveredToTargetDrive;
            //                 break;
            //
            //             default:
            //                 throw new OdinSystemException("Unhandled success scenario in peer transfer");
            //         }
            //     }
            //     else
            //     {
            //         // Map to something to tell the client
            //         switch (result.TransferResult)
            //         {
            //             case TransferResult.RecipientServerError:
            //             case TransferResult.RecipientServerNotResponding:
            //             case TransferResult.UnknownError:
            //                 transferStatus[result.Recipient.DomainName] = TransferStatus.TotalRejectionClientShouldRetry;
            //                 break;
            //
            //             case TransferResult.RecipientDoesNotHavePermissionToFileAcl:
            //                 transferStatus[result.Recipient.DomainName] = TransferStatus.RecipientDoesNotHavePermissionToFileAcl;
            //                 break;
            //
            //             case TransferResult.FileDoesNotAllowDistribution:
            //                 transferStatus[result.Recipient.DomainName] = TransferStatus.FileDoesNotAllowDistribution;
            //                 break;
            //
            //             case TransferResult.RecipientServerReturnedAccessDenied:
            //                 transferStatus[result.Recipient.DomainName] = TransferStatus.RecipientReturnedAccessDenied;
            //                 break;
            //
            //             default:
            //                 throw new ArgumentOutOfRangeException();
            //         }
            //     }
            // }

            return transferStatus;
        }

        private Task<Dictionary<string, TransferStatus>> MapOutboxCreationResult(Dictionary<string, bool> outboxStatus)
        {
            var transferStatus = new Dictionary<string, TransferStatus>();

            foreach (var s in outboxStatus)
            {
                transferStatus.Add(s.Key, s.Value ? TransferStatus.TransferKeyCreated : TransferStatus.AwaitingTransferKey);
            }

            return Task.FromResult(transferStatus);
        }
    }
}