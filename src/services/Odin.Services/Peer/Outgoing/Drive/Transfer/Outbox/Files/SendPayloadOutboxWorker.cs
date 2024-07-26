using System;
using System.Collections.Generic;
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
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendPayloadOutboxWorker(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<SendPayloadOutboxWorker> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory
) : OutboxWorkerBase(fileItem, logger)
{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, DatabaseConnection cn, CancellationToken cancellationToken)
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

            var request = OdinSystemSerializer.Deserialize<SendPayloadRequest>(FileItem.State.Data.ToStringFromUtf8Bytes());
            if (request.InstructionSet.TargetFile.GetFileIdentifierType() == FileIdentifierType.UniqueId)
            {
                throw new OdinOutboxProcessingException("Cannot send payloads by FileIdentifierType.UniqueId")
                {
                    File = FileItem.File,
                    TransferStatus = LatestTransferStatus.InternalServerError,
                    Recipient = default,
                    VersionTag = default,
                    GlobalTransitId = default
                };
            }

            await PerformanceCounter.MeasureExecutionTime("Outbox SendPayload",
                async () => { await SendPayload(request, odinContext, cn, cancellationToken); });

            return (true, UnixTimeUtc.ZeroTime);
        }
        catch (OdinOutboxProcessingException e)
        {
            try
            {
                return await HandleOutboxProcessingException(odinContext, cn, e);
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

    private async Task SendPayload(
        SendPayloadRequest request,
        IOdinContext odinContext,
        DatabaseConnection cn,
        CancellationToken cancellationToken)
    {
        OdinId recipient = FileItem.Recipient;

        var file = FileItem.File;

        var instructionSet = request.InstructionSet;
        var fileSystem = fileSystemResolver.ResolveFileSystem(instructionSet.FileSystemType);
        var readFromTemp = request.InstructionSet.TargetFile.GetFileIdentifierType() == FileIdentifierType.GlobalTransitId;

        var transferInstructionSetStream = new StreamPart(
            value: new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray()),
            fileName: "payloadTransferInstructionSet",
            contentType: "application/json",
            name: Enum.GetName(MultipartHostTransferParts.PayloadTransferInstructionSet));

        var parts = new List<StreamPart>();
        foreach (var payloadDescriptor in request.InstructionSet.Manifest.PayloadDescriptors)
        {
            var payloadKey = payloadDescriptor.PayloadKey;

            var payloadStream = readFromTemp
                ? await GetPayloadStreamFromTemp(odinContext, cn, payloadDescriptor, fileSystem, file)
                : await GetPayloadStream(odinContext, cn, fileSystem, file, payloadKey);

            parts.Add(new StreamPart(payloadStream, payloadKey, payloadDescriptor.ContentType, Enum.GetName(MultipartHostTransferParts.Payload)));

            foreach (var thumb in payloadDescriptor.Thumbnails ?? new List<UploadedManifestThumbnailDescriptor>())
            {
                var thumbnailStream = readFromTemp
                    ? await GetThumbnailStreamFromTemp(odinContext, cn, payloadDescriptor, fileSystem, file, thumb)
                    : await GetThumbnailStream(odinContext, cn, fileSystem, file, thumb, payloadDescriptor);

                var thumbnailKey = thumb.CreateTransitKey(payloadKey);
                parts.Add(new StreamPart(thumbnailStream, thumbnailKey, thumb.ContentType,
                    Enum.GetName(MultipartUploadParts.Thumbnail)));
            }
        }

        var decryptedClientAuthTokenBytes = FileItem.State.EncryptedClientAuthToken;
        var clientAuthToken = ClientAuthenticationToken.FromPortableBytes(decryptedClientAuthTokenBytes);
        decryptedClientAuthTokenBytes.WriteZeros(); //never send the client auth token; even if encrypted
        
        async Task<ApiResponse<PeerTransferResponse>> TrySendFile()
        {
            var client = odinHttpClientFactory.CreateClientUsingAccessToken<IPeerTransferHttpClient>(recipient,clientAuthToken);
            var response = await client.UpdatePayloads(transferInstructionSetStream, parts.ToArray());
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
                return;
            }

            throw new OdinOutboxProcessingException(
                "Failed while sending updated payloads (note: versionTag and GlobalTransitId are for the recipient identity)")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = request.InstructionSet.VersionTag,
                GlobalTransitId = request.InstructionSet.TargetFile.FileId,
                Recipient = recipient,
                File = file
            };
        }
        catch (TryRetryException ex)
        {
            var e = ex.InnerException;
            var status = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                ? LatestTransferStatus.RecipientServerNotResponding
                : LatestTransferStatus.InternalServerError;

            throw new OdinOutboxProcessingException(
                "Failed sending updated payloads to recipient (note: versionTag and GlobalTransitId are for the recipient identity)")
            {
                TransferStatus = status,
                VersionTag = request.InstructionSet.VersionTag,
                GlobalTransitId = request.InstructionSet.TargetFile.FileId,
                Recipient = recipient,
                File = file
            };
        }
    }

    private static async Task<Stream> GetPayloadStream(IOdinContext odinContext, DatabaseConnection cn,
        IDriveFileSystem fileSystem, InternalDriveFileId file, string payloadKey)
    {
        var p = await fileSystem.Storage.GetPayloadStream(file, payloadKey, null, odinContext, cn);
        return p.Stream;
    }

    private static async Task<Stream> GetThumbnailStream(IOdinContext odinContext, DatabaseConnection cn,
        IDriveFileSystem fileSystem, InternalDriveFileId file,
        UploadedManifestThumbnailDescriptor thumb, UploadManifestPayloadDescriptor descriptor)
    {
        var p = await fileSystem.Storage.GetThumbnailPayloadStream(file, thumb.PixelWidth, thumb.PixelHeight, descriptor.PayloadKey,
            descriptor.PayloadUid,
            odinContext, cn);
        return p.stream;
    }

    private static async Task<Stream> GetPayloadStreamFromTemp(IOdinContext odinContext, DatabaseConnection cn,
        UploadManifestPayloadDescriptor descriptor,
        IDriveFileSystem fileSystem, InternalDriveFileId file)
    {
        var payloadExtension = DriveFileUtility.GetPayloadFileExtension(descriptor.PayloadKey, descriptor.PayloadUid);
        var payloadStream = await fileSystem.Storage.GetStreamFromTempFile(file, payloadExtension, odinContext, cn);
        return payloadStream;
    }

    private static async Task<Stream> GetThumbnailStreamFromTemp(IOdinContext odinContext, DatabaseConnection cn,
        UploadManifestPayloadDescriptor descriptor,
        IDriveFileSystem fileSystem, InternalDriveFileId file, UploadedManifestThumbnailDescriptor thumb)
    {
        var extension = DriveFileUtility.GetThumbnailFileExtension(descriptor.PayloadKey, descriptor.PayloadUid, thumb.PixelWidth,
            thumb.PixelHeight);
        var thumbStream = await fileSystem.Storage.GetStreamFromTempFile(file, extension, odinContext, cn);
        return thumbStream;
    }

    protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext, DatabaseConnection cn,
        OdinOutboxProcessingException e)
    {
        return Task.FromResult(CalculateNextRunTime(e.TransferStatus));
    }

    protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e, IOdinContext odinContext, DatabaseConnection cn)
    {
        return Task.CompletedTask;
    }
}