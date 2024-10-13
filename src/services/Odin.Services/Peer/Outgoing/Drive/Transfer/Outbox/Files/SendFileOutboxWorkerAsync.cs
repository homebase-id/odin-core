using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendFileOutboxWorkerAsync(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<SendFileOutboxWorkerAsync> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory
) : OutboxWorkerBase(fileItem, logger, fileSystemResolver)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, IdentityDatabase db, CancellationToken cancellationToken)
    {
        try
        {
            if (FileItem.AttemptCount > odinConfiguration.Host.PeerOperationMaxAttempts)
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

            logger.LogDebug("Start: Sending file: {file} to {recipient}", FileItem.File, FileItem.Recipient);

            Guid versionTag = default;
            Guid globalTransitId = default;

            await PerformanceCounter.MeasureExecutionTime("Outbox SendOutboxFileItemAsync",
                async () => { (versionTag, globalTransitId) = await SendOutboxFileItemAsync(FileItem, odinContext, db, cancellationToken); });

<<<<<<< HEAD
            await UpdateFileTransferHistory(globalTransitId, versionTag, odinContext, cn);
            logger.LogDebug("Successful transfer of {gtid} to {recipient} - ", globalTransitId, FileItem.Recipient);
=======
            logger.LogDebug("Success Sending file: {file} to {recipient} with gtid: {gtid}", FileItem.File, FileItem.Recipient, globalTransitId);

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

            logger.LogDebug("Successful transfer of {gtid} to {recipient} - " +
                            "Action: Marking Complete (popStamp:{marker})", globalTransitId, FileItem.Recipient, FileItem.Marker);
>>>>>>> main

            return (true, UnixTimeUtc.ZeroTime);
        }
        catch (OdinOutboxProcessingException e)
        {
            try
            {
                return await HandleOutboxProcessingException(odinContext, db, e);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error while handling the outbox processing exception " +
                                           "for file: {file} and recipient: {recipient} with version: " +
                                           "{version} and status: {status}",
                    e.File,
                    e.Recipient,
                    e.TransferStatus,
                    e.VersionTag);

                throw new OdinSystemException("Failed while handling Outbox Processing Exception", e);
            }
        }
    }

    private async Task<(Guid versionTag, Guid globalTransitId)> SendOutboxFileItemAsync(OutboxFileItem outboxFileItem, IOdinContext odinContext,
        IdentityDatabase db,
        CancellationToken cancellationToken)
    {
        OdinId recipient = outboxFileItem.Recipient;
        var file = outboxFileItem.File;
        var options = outboxFileItem.State.OriginalTransitOptions;

        var instructionSet = FileItem.State.TransferInstructionSet;
<<<<<<< HEAD
        var fileSystem = FileSystemResolver.ResolveFileSystem(instructionSet.FileSystemType);
        var header = await fileSystem.Storage.GetServerFileHeader(outboxFileItem.File, odinContext, cn);
=======
        var fileSystem = fileSystemResolver.ResolveFileSystem(instructionSet.FileSystemType);

        var header = await fileSystem.Storage.GetServerFileHeader(outboxFileItem.File, odinContext, db);
>>>>>>> main
        var versionTag = header.FileMetadata.VersionTag.GetValueOrDefault();
        var globalTransitId = header.FileMetadata.GlobalTransitId;

        if (header.ServerMetadata.AllowDistribution == false)
        {
            throw new OdinOutboxProcessingException("File does not allow distribution")
            {
                TransferStatus = LatestTransferStatus.SourceFileDoesNotAllowDistribution,
                VersionTag = versionTag,
                Recipient = recipient,
                File = file
            };
        }

        if (options.UseAppNotification)
        {
            instructionSet.AppNotificationOptions = options.AppNotificationOptions;
        }

        var redactedAcl = header.ServerMetadata.AccessControlList;
        redactedAcl?.OdinIdList?.Clear();
        instructionSet.OriginalAcl = redactedAcl;

        var transferKeyHeaderStream = new StreamPart(
            new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray()),
            "transferInstructionSet.encrypted", "application/json",
            Enum.GetName(MultipartHostTransferParts.TransferKeyHeader));

<<<<<<< HEAD
        var shouldSendPayload = options.SendContents.HasFlag(SendContents.Payload);
        var (metaDataStream, payloadStreams) = await PackageFileStreams(header, shouldSendPayload, odinContext, cn, options.OverrideRemoteGlobalTransitId);
=======
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
            OriginalAuthor = sourceMetadata.OriginalAuthor,
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
                var p = await fileSystem.Storage.GetPayloadStream(file, payloadKey, null, odinContext, db);
                var payloadStream = p.Stream;

                var payload = new StreamPart(payloadStream, payloadKey, contentType, Enum.GetName(MultipartHostTransferParts.Payload));
                additionalStreamParts.Add(payload);

                foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
                {
                    var (thumbStream, thumbHeader) =
                        await fileSystem.Storage.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight, descriptor.Key, descriptor.Uid,
                            odinContext, db);

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
>>>>>>> main

        var decryptedClientAuthTokenBytes = outboxFileItem.State.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.Wipe(); //never send the client auth token; even if encrypted
        
        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient, clientAuthToken);
            var response = await client.SendHostToHost(transferKeyHeaderStream, metaDataStream, payloadStreams.ToArray());
            return response;
        }

        try
        {
            ApiResponse<PeerTransferResponse> response = null;

            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                odinConfiguration.Host.PeerOperationDelayMs,
                cancellationToken,
                async () => { response = await TrySendFile(); });

            if (response.IsSuccessStatusCode)
            {
                return (versionTag, globalTransitId.GetValueOrDefault());
            }

            throw new OdinOutboxProcessingException("Failed while sending the request")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = versionTag,
                GlobalTransitId = globalTransitId,
                Recipient = recipient,
                File = file
            };
        }
        catch (TryRetryException ex)
        {
            var e = ex.InnerException;
            var status = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                ? LatestTransferStatus.RecipientServerNotResponding
                : LatestTransferStatus.UnknownServerError;

            throw new OdinOutboxProcessingException("Failed sending to recipient")
            {
                TransferStatus = status,
                VersionTag = versionTag,
                Recipient = recipient,
                GlobalTransitId = globalTransitId,
                File = file
            };
        }
    }

<<<<<<< HEAD

    protected override async Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, DatabaseConnection cn,
=======
    protected override async Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, IdentityDatabase db,
>>>>>>> main
        OdinOutboxProcessingException e)
    {
        logger.LogDebug(e, "Recoverable: Updating TransferHistory file {file} to status {status}.", e.File, e.TransferStatus);

        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = true,
            LatestTransferStatus = e.TransferStatus,
            VersionTag = null
        };

        var nextRunTime = CalculateNextRunTime(e.TransferStatus);
<<<<<<< HEAD
        var fs = FileSystemResolver.ResolveFileSystem(FileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext, cn);
=======
        var fs = fileSystemResolver.ResolveFileSystem(FileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext, db);
>>>>>>> main

        return nextRunTime;
    }

    protected override async Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
        IOdinContext odinContext,
        IdentityDatabase db)
    {
        logger.LogDebug(e, "Unrecoverable: Updating TransferHistory file {file} to status {status}.", e.File, e.TransferStatus);

        var update = new UpdateTransferHistoryData()
        {
            IsInOutbox = false,
            LatestTransferStatus = e.TransferStatus,
            VersionTag = null
        };

<<<<<<< HEAD
        var fs = FileSystemResolver.ResolveFileSystem(FileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext, cn);
=======
        var fs = fileSystemResolver.ResolveFileSystem(FileItem.State.TransferInstructionSet.FileSystemType);
        await fs.Storage.UpdateTransferHistory(FileItem.File, FileItem.Recipient, update, odinContext, db);
>>>>>>> main
    }
}