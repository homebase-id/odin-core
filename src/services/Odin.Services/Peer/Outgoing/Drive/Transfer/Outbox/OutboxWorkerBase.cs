using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public abstract class OutboxWorkerBase(OutboxFileItem fileItem, ILogger logger, FileSystemResolver fileSystemResolver, OdinConfiguration odinConfiguration)
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
    protected async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> HandleOutboxProcessingException(IOdinContext odinContext,
        IdentityDatabase db,
        OdinOutboxProcessingException e)
    {
        logger.LogDebug(e, "Failed to process outbox item for recipient: {recipient} " +
                           "with globalTransitId:{gtid}.  Transfer status was {transferStatus}",
            e.Recipient,
            e.GlobalTransitId,
            e.TransferStatus);


        switch (e.TransferStatus)
        {
            case LatestTransferStatus.RecipientIdentityReturnedAccessDenied:
            case LatestTransferStatus.UnknownServerError:
            case LatestTransferStatus.RecipientIdentityReturnedBadRequest:
            case LatestTransferStatus.SendingServerTooManyAttempts:
                logger.LogDebug(e, "Unrecoverable Error for file {file} to recipient:{recipient}", fileItem.File, FileItem.Recipient);
                PerformanceCounter.IncrementCounter("Outbox Unrecoverable Error");
                await HandleUnrecoverableTransferStatus(e, odinContext, db);
                return (true, UnixTimeUtc.ZeroTime);

            case LatestTransferStatus.RecipientIdentityReturnedServerError:
            case LatestTransferStatus.RecipientServerNotResponding:
            case LatestTransferStatus.SourceFileDoesNotAllowDistribution:
                logger.LogDebug(e, "Recoverable Error for file {file} to recipient:{recipient}", fileItem.File, FileItem.Recipient);
                PerformanceCounter.IncrementCounter("Outbox Recoverable Error");
                var nextRun = await HandleRecoverableTransferStatus(odinContext, db, e);
                return (false, nextRun);

            default:
                throw new OdinSystemException("Unhandled LatestTransferStatus");
        }
    }

    protected abstract Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, IdentityDatabase db,
        OdinOutboxProcessingException e);

    protected abstract Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
        IOdinContext odinContext,
        IdentityDatabase db);

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
        var delay = CalculateSecondsDelay(FileItem.AttemptCount);

        switch (transferStatus)
        {
            case LatestTransferStatus.RecipientIdentityReturnedServerError:
            case LatestTransferStatus.RecipientServerNotResponding:
                return UnixTimeUtc.Now().AddSeconds(delay);

            case LatestTransferStatus.SourceFileDoesNotAllowDistribution:
                return UnixTimeUtc.Now().AddSeconds(delay);
            default:
                return UnixTimeUtc.Now().AddSeconds(30);
        }
    }

    protected async Task<(StreamPart metadataStream, List<StreamPart> payloadStreams)> PackageFileStreamsAsync(
        ServerFileHeader header,
        bool includePayloads,
        IOdinContext odinContext,
        IdentityDatabase db,
        Guid? overrideGlobalTransitId = null
    )
    {
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
        };

        var json = OdinSystemSerializer.Serialize(redactedMetadata);
        var stream = new MemoryStream(json.ToUtf8ByteArray());
        var metaDataStream = new StreamPart(stream, "metadata.encrypted", "application/json",
            Enum.GetName(MultipartHostTransferParts.Metadata));

        var payloadStreams = new List<StreamPart>();

        if (includePayloads)
        {
            foreach (var descriptor in redactedMetadata.Payloads ?? new List<PayloadDescriptor>())
            {
                var payloadKey = descriptor.Key;

                string contentType = "application/unknown";

                //TODO: consider what happens if the payload has been delete from disk
                var p = await fileSystem.Storage.GetPayloadStreamAsync(file, payloadKey, null, odinContext, db);
                var payloadStream = p.Stream;

                var payload = new StreamPart(payloadStream, payloadKey, contentType, Enum.GetName(MultipartHostTransferParts.Payload));
                payloadStreams.Add(payload);

                foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                {
                    var (thumbStream, thumbHeader) =
                        await fileSystem.Storage.GetThumbnailPayloadStreamAsync(file, thumb.PixelWidth, thumb.PixelHeight, descriptor.Key,
                            descriptor.Uid,
                            odinContext, db);

                    var thumbnailKey =
                        $"{payloadKey}" +
                        $"{DriveFileUtility.TransitThumbnailKeyDelimiter}" +
                        $"{thumb.PixelWidth}" +
                        $"{DriveFileUtility.TransitThumbnailKeyDelimiter}" +
                        $"{thumb.PixelHeight}";

                    payloadStreams.Add(new StreamPart(thumbStream, thumbnailKey, thumbHeader.ContentType,
                        Enum.GetName(MultipartUploadParts.Thumbnail)));
                }
            }
        }

        return (metaDataStream, payloadStreams);
    }

    protected async Task UpdateFileTransferHistory(Guid globalTransitId, Guid versionTag, IOdinContext odinContext, IdentityDatabase db)
    {
        logger.LogDebug("Success Sending file: {file} to {recipient} with gtid: {gtid}", FileItem.File, FileItem.Recipient,
            globalTransitId);

        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = false,
            IsReadByRecipient = false,
            LatestTransferStatus = LatestTransferStatus.Delivered,
            VersionTag = versionTag
        };

        logger.LogDebug("Start: UpdateTransferHistory: {file} to {recipient} " +
                        "with gtid: {gtid}", FileItem.File, FileItem.Recipient, globalTransitId);

        var fs = fileSystemResolver.ResolveFileSystem(FileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext, db);

        logger.LogDebug("Success: UpdateTransferHistory: {file} to {recipient} " +
                        "with gtid: {gtid}", FileItem.File, FileItem.Recipient, globalTransitId);
    }

    private int CalculateSecondsDelay(int attemptNumber)
    {
        int baseDelaySeconds = 10;

        if (attemptNumber <= 5)
        {
            return (int)(baseDelaySeconds  * attemptNumber);
        }

        baseDelaySeconds = 30;
        return (int)(baseDelaySeconds  * attemptNumber);
    }
}