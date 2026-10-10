using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public abstract class OutboxWorkerBase(
    OutboxFileItem fileItem,
    ILogger logger,
    FileSystemResolver fileSystemResolver,
    OdinConfiguration odinConfiguration)
{
    protected OutboxFileItem FileItem => fileItem;
    protected FileSystemResolver FileSystemResolver => fileSystemResolver;

    protected readonly OdinConfiguration Configuration = odinConfiguration;

    protected void AssertHasRemainingAttempts()
    {
        if (FileItem.AttemptCount > Configuration.Host.OutboxOperationMaxAttempts)
        {
            throw new OdinOutboxProcessingException("Too many attempts")
            {
                File = FileItem.File,
                TransferStatus = LatestTransferStatus.SendingServerTooManyAttempts,
                Recipient = default,
                VersionTag = default,
                GlobalTransitId = default
            };
        }
    }

    /// <summary>
    /// Runs <paramref name="send"/> and turns an <see cref="OdinOutboxProcessingException"/> into the
    /// scheduling decision from <see cref="HandleOutboxProcessingException"/>. Without this, the processor's
    /// last-resort catch reschedules the item at "now" and it spins through its attempts in seconds; a
    /// recipient that is paused or out of quota is waited out instead.
    /// </summary>
    protected async Task<OutboxProcessingResult> SendHandledAsync(
        Func<IOdinContext, CancellationToken, Task<OutboxProcessingResult>> send,
        IOdinContext odinContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send(odinContext, cancellationToken);
        }
        catch (OdinOutboxProcessingException e)
        {
            return await HandleOutboxProcessingException(odinContext, e);
        }
    }

    protected async Task<OutboxProcessingResult> HandleOutboxProcessingException(IOdinContext odinContext,
        OdinOutboxProcessingException e)
    {
        logger.LogDebug(e, "Failed to process outbox item (type: {type}) for recipient: {recipient} " +
                           "with globalTransitId:{gtid}.  Transfer status was {transferStatus}",
            FileItem.Type,
            e.Recipient,
            e.GlobalTransitId,
            e.TransferStatus);

        switch (e.TransferStatus)
        {
            case LatestTransferStatus.RecipientIdentityReturnedAccessDenied:
            case LatestTransferStatus.UnknownServerError:
            case LatestTransferStatus.RecipientIdentityReturnedBadRequest:
            case LatestTransferStatus.SendingServerTooManyAttempts:
                return await GiveUpAsync(odinContext, e);

            case LatestTransferStatus.RecipientIdentityReturnedServerError:
            case LatestTransferStatus.RecipientServerNotResponding:
            case LatestTransferStatus.SourceFileDoesNotAllowDistribution:
                if (e.RetryAfter.HasValue)
                {
                    return await RetryLaterAsync(odinContext, e, e.RetryAfter.Value);
                }

                logger.LogDebug(e, "Recoverable Error for file {file} to recipient:{recipient}", fileItem.File, FileItem.Recipient);
                PerformanceCounter.IncrementCounter("Outbox Recoverable Error");
                var nextRun = await HandleRecoverableTransferStatus(odinContext, e);
                return OutboxProcessingResult.Retry(nextRun);

            default:
                throw new OdinSystemException("Unhandled LatestTransferStatus");
        }
    }

    /// <summary>
    /// The recipient is paused or out of quota and told us when to come back. Wait it out instead of
    /// spending attempts, but not forever: past <see cref="OdinConfiguration.HostSection.OutboxRetryLaterMaxAge"/>
    /// the item is given up on.
    /// </summary>
    private async Task<OutboxProcessingResult> RetryLaterAsync(IOdinContext odinContext, OdinOutboxProcessingException e,
        TimeSpan retryAfter)
    {
        var now = UnixTimeUtc.Now();
        if (OutboxRetryLater.IsExpired(FileItem.AddedTimestamp, now, Configuration.Host.OutboxRetryLaterMaxAge))
        {
            logger.LogInformation(
                "Recipient {recipient} has been asking us to retry later since {added}; giving up on file {file}",
                FileItem.Recipient, FileItem.AddedTimestamp, FileItem.File);

            e.TransferStatus = LatestTransferStatus.SendingServerTooManyAttempts;
            return await GiveUpAsync(odinContext, e);
        }

        var nextRunTime = OutboxRetryLater.NextRun(retryAfter, now);
        logger.LogDebug("Recipient {recipient} asked us to retry after {retryAfter}; next attempt at {nextRun}",
            FileItem.Recipient, retryAfter, nextRunTime);

        PerformanceCounter.IncrementCounter("Outbox Retry Later");
        await HandleRecoverableTransferStatus(odinContext, e);
        return OutboxProcessingResult.RetryLater(nextRunTime);
    }

    private async Task<OutboxProcessingResult> GiveUpAsync(IOdinContext odinContext, OdinOutboxProcessingException e)
    {
        logger.LogDebug(e, "Unrecoverable Error for file {file} to recipient:{recipient}", fileItem.File, FileItem.Recipient);
        PerformanceCounter.IncrementCounter("Outbox Unrecoverable Error");
        await HandleUnrecoverableTransferStatus(e, odinContext);
        return OutboxProcessingResult.Complete();
    }

    protected abstract Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext,
        OdinOutboxProcessingException e);

    protected abstract Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
        IOdinContext odinContext);

    protected LatestTransferStatus MapPeerErrorResponseHttpStatus<T>(ApiResponse<T> response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return LatestTransferStatus.RecipientIdentityReturnedAccessDenied;
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            logger.LogDebug("BadRequest received: [{data}]", response.Error?.Content);
            return LatestTransferStatus.RecipientIdentityReturnedBadRequest;
        }

        return LatestTransferStatus.RecipientIdentityReturnedServerError;
    }

    protected UnixTimeUtc CalculateNextRunTime(LatestTransferStatus transferStatus)
    {
        switch (transferStatus)
        {
            case LatestTransferStatus.RecipientIdentityReturnedServerError:
            case LatestTransferStatus.RecipientServerNotResponding:
                return CalculateBackoffNextRunTime();

            case LatestTransferStatus.SourceFileDoesNotAllowDistribution:
                return CalculateBackoffNextRunTime();
            default:
                return UnixTimeUtc.Now().AddSeconds(30);
        }
    }

    /// <summary>
    /// Next run for a retry, backing off with the item's attempt count: 10 s steps for the first five
    /// attempts, 30 s steps after that.
    /// </summary>
    protected UnixTimeUtc CalculateBackoffNextRunTime()
    {
        return UnixTimeUtc.Now().AddSeconds(CalculateSecondsDelay(FileItem.AttemptCount));
    }

    protected async Task<(Stream metadataStream,
            StreamPart metadataStreamPart,
            List<Stream> payloadStreams,
            List<StreamPart> payloadStreamParts)>
        PackageFileStreamsAsync(
            ServerFileHeader header,
            IOdinContext odinContext,
            Guid? overrideGlobalTransitId = null,
            DataSource datasourceOverride = null)
    {
        // header.FileMetadata.Validate(odinContext.Tenant);
        
        var sourceMetadata = header.FileMetadata;

        var file = header.FileMetadata.File;
        var fileSystem = FileSystemResolver.ResolveFileSystem(header.ServerMetadata.FileSystemType);

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
            GlobalTransitId = overrideGlobalTransitId.GetValueOrDefault(header.FileMetadata.GlobalTransitId.GetValueOrDefault()),
            ReactionPreview = sourceMetadata.ReactionPreview,
            SenderOdinId = sourceMetadata.SenderOdinId,
            OriginalAuthor = sourceMetadata.OriginalAuthor,
            ReferencedFile = sourceMetadata.ReferencedFile,
            VersionTag = sourceMetadata.VersionTag,
            Payloads = sourceMetadata.Payloads,
            FileState = sourceMetadata.FileState,
            DataSource = datasourceOverride ?? sourceMetadata.DataSource,

            // Retention must travel: the recipient schedules the deletion of their own copy from this
            // value, which is how a group expires a message without any retention message being sent.
            // An absolute Ttl makes every copy die at the same moment; a negative one arrives still
            // pending so each copy runs its own clock from its own reader's first view.
            Ttl = sourceMetadata.Ttl
        };

        var json = OdinSystemSerializer.Serialize(redactedMetadata);
        var metaDataStream = new MemoryStream(json.ToUtf8ByteArray());
        var metaDataStreamPart = new StreamPart(metaDataStream, "metadata.encrypted", "application/json",
            Enum.GetName(MultipartHostTransferParts.Metadata));

        var payloadStreams = new List<Stream>();
        var payloadStreamParts = new List<StreamPart>();

        // Every part is opened here, up front, before the request starts. Since #1892 these are open files or S3
        // responses, not in-memory copies, so if a later one fails to open, the ones already open are closed below
        // rather than left to the GC.
        //
        // Known cost of opening up front (inferred, not observed): on S3, the responses for later parts sit unread
        // while an earlier, large part uploads to the peer, and an idle timeout on the S3 side or a proxy could close
        // them. That shows up as a failed send that is given up, not retried.
        //
        // Opening each part only as it is sent avoids that, and was built and taken out again (PR for #1892) after
        // three review passes kept finding edges in it. What a lazy version has to get right:
        // - Open every part at the snapshot's version (key and uid), thumbnails included. GetPayloadStreamAsync and
        //   GetThumbnailPayloadStreamAsync read the CURRENT header, so a payload replaced or deleted mid-send goes out
        //   under the snapshot's IV, or as a silent 0-byte thumbnail (Stream.Null).
        // - Check that every part exists before connecting, or a missing one is found only after uploading every part
        //   ahead of it, on every retry. One check per part per recipient per retry is a round trip each on S3.
        // - A part that fails to open or read mid-send comes out of HttpClient as an HttpRequestException ("error while
        //   copying content"), which the workers classify as RecipientServerNotResponding (retried), not
        //   UnknownServerError (given up). Keep the original failure and rethrow it.
        // - The stream must refuse to open after it is disposed, and pass the read's cancellation token to the open.
        // - The open's DB and permission checks run inside the HTTP send, on the scoped connection, and count against
        //   the peer client's timeout.
        // - A stream of unknown length makes the multipart request chunked (no Content-Length); opening up front keeps
        //   Content-Length on disk-backed storage.
        // - On the feed drive a missing thumbnail is sent as an empty part today (Stream.Null); a check up front must
        //   not turn that into a failed transfer.
        try
        {
            var shouldSendPayloads = !redactedMetadata.PayloadsAreRemote;
            if (shouldSendPayloads)
            {
                foreach (var descriptor in redactedMetadata.Payloads ?? new List<PayloadDescriptor>())
                {
                    var payloadKey = descriptor.Key;

                    string contentType = "application/unknown";

                    //TODO: consider what happens if the payload has been delete from disk

                    // NOTE: caller takes ownership of the stream inside 'p' and is responsible for disposing
                    var p = await fileSystem.Storage.GetPayloadStreamAsync(file, payloadKey, null, odinContext);
                    var payloadStream = p.Stream;
                    payloadStreams.Add(payloadStream);

                    var payload = new StreamPart(payloadStream, payloadKey, contentType, Enum.GetName(MultipartHostTransferParts.Payload));
                    payloadStreamParts.Add(payload);

                    foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                    {
                        var (thumbStream, thumbHeader) = await fileSystem.Storage.GetThumbnailPayloadStreamAsync(file, thumb.PixelWidth,
                            thumb.PixelHeight, descriptor.Key,
                            descriptor.Uid,
                            odinContext);

                        payloadStreams.Add(thumbStream);

                        var thumbnailKey = $"{payloadKey}" +
                                           $"{TenantPathManager.TransitThumbnailKeyDelimiter}" +
                                           $"{thumb.PixelWidth}" +
                                           $"{TenantPathManager.TransitThumbnailKeyDelimiter}" +
                                           $"{thumb.PixelHeight}";

                        payloadStreamParts.Add(new StreamPart(thumbStream, thumbnailKey, thumbHeader.ContentType,
                            Enum.GetName(MultipartUploadParts.Thumbnail)));
                    }
                }
            }
        }
        catch
        {
            foreach (var stream in payloadStreams)
            {
                await stream.DisposeAsync();
            }

            throw;
        }

        return (metaDataStream, metaDataStreamPart, payloadStreams, payloadStreamParts);
    }

    protected async Task UpdateFileTransferHistory(Guid globalTransitId, Guid versionTag, IOdinContext odinContext)
    {
        logger.LogDebug("Success Sending file: {file} to {recipient} with gtid: {gtid}", FileItem.File, FileItem.Recipient,
            globalTransitId);

        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = false,
            ReadByRecipientTimestamp = 0,
            LatestTransferStatus = LatestTransferStatus.Delivered,
            VersionTag = versionTag
        };

        logger.LogDebug("Start: UpdateTransferHistory: {file} to {recipient} " +
                        "with gtid: {gtid}", FileItem.File, FileItem.Recipient, globalTransitId);

        var fs = fileSystemResolver.ResolveFileSystem(FileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext, null);

        logger.LogDebug("Success: UpdateTransferHistory: {file} to {recipient} " +
                        "with gtid: {gtid}", FileItem.File, FileItem.Recipient, globalTransitId);
    }

    private int CalculateSecondsDelay(int attemptNumber)
    {
        int baseDelaySeconds = 10;

        if (attemptNumber < 1)
        {
            attemptNumber = 1;
        }

        if (attemptNumber <= 5)
        {
            return (int)(baseDelaySeconds * attemptNumber);
        }

        baseDelaySeconds = 30;
        return (int)(baseDelaySeconds * attemptNumber);
    }
}