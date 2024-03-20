using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Quartz;
using Refit;

namespace Odin.Services.Peer.Outgoing.Jobs;
#nullable enable

internal static class Keys
{
    public const string OutboxJsonProcessingResultKey = "outboxProcessingResult";
    public const string OutboxJsonItemKey = "outboxItemJson";
}

public class OutboxProcessingJob(
    OdinId sender,
    TransitOutboxItem item,
    OdinConfiguration configuration,
    SchedulerGroup schedulerGroup = SchedulerGroup.Default) : AbstractJobSchedule
{
    public sealed override string SchedulingKey { get; } =
        ByteArrayUtil.ReduceSHA256Hash($"{sender}{item.File.DriveId}{item.File.FileId}{item.Recipient}").ToString();

    public override SchedulerGroup SchedulerGroup { get; } = schedulerGroup;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        jobBuilder
            .WithRetry(configuration.Host.PeerTransferOperationRetryAttempts, configuration.Host.PeerTransferRetryDelaySeconds)
            .WithRetention(configuration.Host.PeerTransferOperationRetentionMinutes)
            .WithJobEvent<OutboxProcessingJobCompletedEvent>()
            .UsingJobData(Keys.OutboxJsonItemKey, OdinSystemSerializer.Serialize(item));

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create().StartNow()
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

public class OutboxItemProcessorJob(
    ICorrelationContext correlationContext,
    IOdinHttpClientFactory odinHttpClientFactory,
    FileSystemResolver fileSystemResolver,
    IDriveAclAuthorizationService driveAclAuthorizationService,
    ILogger<OutboxItemProcessorJob> logger)
    : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString(Keys.OutboxJsonItemKey, out var json) && json != null)
        {
            var item = OdinSystemSerializer.Deserialize<TransitOutboxItem>(json);
            
            logger.LogDebug("OutboxItemProcessorJob running {x}", item!.File);
            
            var result = await SendOutboxItemAsync(item);
            await context.UpdateJobMap(Keys.OutboxJsonProcessingResultKey, OdinSystemSerializer.Serialize(result));
        }
    }

    private async Task<OutboxProcessingResult> SendOutboxItemAsync(TransitOutboxItem outboxItem)
    {
        IDriveFileSystem fs = fileSystemResolver.ResolveFileSystem(outboxItem.TransferInstructionSet.FileSystemType);

        OdinId recipient = outboxItem.Recipient;
        var file = outboxItem.File;
        var options = outboxItem.OriginalTransitOptions;

        var header = await fs.Storage.GetServerFileHeader(outboxItem.File);

        // Enforce ACL at the last possible moment before shipping the file out of the identity; in case it changed
        if (!await driveAclAuthorizationService.IdentityHasPermission(recipient, header.ServerMetadata.AccessControlList))
        {
            return new OutboxProcessingResult()
            {
                File = file,
                Recipient = recipient,
                Timestamp = UnixTimeUtc.Now().milliseconds,
                TransferResult = TransferResult.RecipientDoesNotHavePermissionToFileAcl,
                OutboxItem = outboxItem
            };
        }

        var transferInstructionSet = outboxItem.TransferInstructionSet;
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
            return new OutboxProcessingResult()
            {
                File = file,
                Recipient = recipient,
                Timestamp = UnixTimeUtc.Now().milliseconds,
                TransferResult = TransferResult.FileDoesNotAllowDistribution,
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

        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient, clientAuthToken);
            var response = await client.SendHostToHost(transferKeyHeaderStream, metaDataStream, additionalStreamParts.ToArray());
            return response;
        }

        try
        {
            PeerResponseCode peerCode = PeerResponseCode.Unknown;
            TransferResult transferResult = TransferResult.UnknownError;

            // This TryRetry is meant to handle network blips; 
            // attempts * delay will be the max amount a thread pool slot is used by this job; so keep the delay and attempts very small
            // and these are intentionally hard-coded in v1
            await TryRetry.WithDelayAsync(
                attempts: 1,
                delay: TimeSpan.FromMilliseconds(100),
                CancellationToken.None,
                async () => { (peerCode, transferResult) = OutboxProcessingUtils.MapPeerResponseCode(await TrySendFile()); });

            return new OutboxProcessingResult()
            {
                File = file,
                Recipient = recipient,
                RecipientPeerResponseCode = peerCode,
                TransferResult = transferResult,
                Timestamp = UnixTimeUtc.Now().milliseconds,
                OutboxItem = outboxItem
            };
        }
        catch (TryRetryException ex)
        {
            var e = ex.InnerException;
            var tr = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                ? TransferResult.RecipientServerNotResponding
                : TransferResult.UnknownError;

            return new OutboxProcessingResult()
            {
                File = file,
                Recipient = recipient,
                RecipientPeerResponseCode = null,
                TransferResult = tr,
                Timestamp = UnixTimeUtc.Now().milliseconds,
                OutboxItem = outboxItem
            };
        }
    }

}