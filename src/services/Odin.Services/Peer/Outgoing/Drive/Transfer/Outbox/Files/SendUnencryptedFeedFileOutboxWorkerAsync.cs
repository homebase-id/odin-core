using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription;
using Odin.Services.DataSubscription.SendingHost;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Mediator;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

public class SendUnencryptedFeedFileOutboxWorkerAsync(
    OutboxFileItem fileItem,
    FileSystemResolver fileSystemResolver,
    ILogger<SendUnencryptedFeedFileOutboxWorkerAsync> logger,
    OdinConfiguration odinConfiguration,
    IOdinHttpClientFactory odinHttpClientFactory,
    IDriveAclAuthorizationService driveAcl
) : OutboxWorkerBase(fileItem, logger, null, odinConfiguration)

{
    public async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> Send(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        try
        {
            AssertHasRemainingAttempts();

            logger.LogDebug("SendFeedItem -> Sending file: {file} to {recipient}", FileItem.File, FileItem.Recipient);

            var (versionTag, globalTransitId) = await HandleFeedItemAsync(FileItem, odinContext, cancellationToken);

            logger.LogDebug("SendFeedItem -> Successful transfer of {gtid} (version:{version}) to {recipient} - Action: " +
                            "Marking Complete (popStamp:{marker})",
                globalTransitId,
                versionTag,
                FileItem.Recipient,
                FileItem.Marker);

            return (true, UnixTimeUtc.ZeroTime);
        }
        catch (OdinOutboxProcessingException e)
        {
            try
            {
                return await HandleOutboxProcessingException(odinContext, e);
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
                throw;
            }
        }
    }

    private async Task<(Guid versionTag, Guid globalTransitId)> HandleFeedItemAsync(OutboxFileItem outboxFileItem, IOdinContext odinContext,
         CancellationToken cancellationToken)
    {
        OdinId recipient = outboxFileItem.Recipient;
        var file = outboxFileItem.File;

        var distroItem = OdinSystemSerializer.Deserialize<FeedDistributionItem>(outboxFileItem.State.Data.ToStringFromUtf8Bytes());
        var fs = await fileSystemResolver.ResolveFileSystem(file, odinContext);
        var header = await fs.Storage.GetServerFileHeader(file, odinContext);

        if (header == null)
        {
            throw new OdinFileReadException($"File Source {file} file does not exist");
        }

        var versionTag = header.FileMetadata.VersionTag;
        var globalTransitId = header.FileMetadata.GlobalTransitId;

        var authorized = await driveAcl.IdentityHasPermissionAsync(recipient,
            header.ServerMetadata.AccessControlList, odinContext);

        if (!authorized)
        {
            throw new OdinOutboxProcessingException("Failed sending to recipient")
            {
                TransferStatus = LatestTransferStatus.SourceFileDoesNotAllowDistribution,
                VersionTag = header.FileMetadata.VersionTag.GetValueOrDefault(),
                Recipient = recipient,
                GlobalTransitId = header.FileMetadata.GlobalTransitId,
                File = file
            };
        }

        try
        {
            ApiResponse<PeerTransferResponse> response;
            switch (distroItem.DriveNotificationType)
            {
                case DriveNotificationType.FileAdded:
                case DriveNotificationType.FileModified:
                    response = await SendFile(header, distroItem, recipient, cancellationToken);
                    break;

                case DriveNotificationType.FileDeleted:
                    response = await DeleteFile(header, recipient, distroItem.FileSystemType, cancellationToken);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (response.IsSuccessStatusCode)
            {
                return (versionTag.GetValueOrDefault(), globalTransitId.GetValueOrDefault());
            }

            throw new OdinOutboxProcessingException("Failed while sending the request")
            {
                TransferStatus = MapPeerErrorResponseHttpStatus(response),
                VersionTag = versionTag.GetValueOrDefault(),
                GlobalTransitId = globalTransitId,
                Recipient = recipient,
                File = file
            };
        }
        catch (TryRetryException ex)
        {
            var e = ex.InnerException;
            
            logger.LogDebug(e, "Failed processing outbox item (type={t}) from outbox. Message {e}", FileItem.Type, e.Message);

            if (e is HttpRequestException httpRequestException)
            {
                logger.LogDebug("HttpRequestException Error {e} and status code: {status}", httpRequestException.HttpRequestError,
                    httpRequestException.StatusCode);
            }

            var status = (e is TaskCanceledException or HttpRequestException or OperationCanceledException)
                ? LatestTransferStatus.RecipientServerNotResponding
                : LatestTransferStatus.UnknownServerError;

            throw new OdinOutboxProcessingException("Failed sending to recipient")
            {
                TransferStatus = status,
                VersionTag = versionTag.GetValueOrDefault(),
                Recipient = recipient,
                GlobalTransitId = globalTransitId,
                File = file
            };
        }
    }

    private async Task<ApiResponse<PeerTransferResponse>> SendFile(ServerFileHeader header, 
        FeedDistributionItem distroItem, 
        OdinId recipient,
        CancellationToken cancellationToken)
    {
        var request = new UpdateFeedFileMetadataRequest()
        {
            FileId = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = SystemDriveConstants.FeedDrive
            },
            UniqueId = header.FileMetadata.AppData.UniqueId,
            FileMetadata = header.FileMetadata,
            FeedDistroType = distroItem.FeedDistroType,
            EncryptedPayload = distroItem.EncryptedPayload
        };

        var client = odinHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: distroItem.FileSystemType);
        ApiResponse<PeerTransferResponse> httpResponse = null;

        await TryRetry.Create()
            .WithAttempts(Configuration.Host.PeerOperationMaxAttempts)
            .WithDelay(Configuration.Host.PeerOperationDelayMs)
            .WithCancellation(cancellationToken)
            .ExecuteAsync(async () => { httpResponse = await client.SendFeedFileMetadata(request); });

        return httpResponse;
    }

    private async Task<ApiResponse<PeerTransferResponse>> DeleteFile(ServerFileHeader header, OdinId recipient, FileSystemType fileSystemType,
        CancellationToken cancellationToken)
    {
        var request = new DeleteFeedFileMetadataRequest()
        {
            FileId = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = SystemDriveConstants.FeedDrive
            },
            UniqueId = header.FileMetadata.AppData.UniqueId,
        };

        var client = odinHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);
        ApiResponse<PeerTransferResponse> httpResponse = null;

        await TryRetry.Create()
            .WithAttempts(Configuration.Host.PeerOperationMaxAttempts)
            .WithDelay(Configuration.Host.PeerOperationDelayMs)
            .WithCancellation(cancellationToken)
            .ExecuteAsync(async () => { httpResponse = await client.DeleteFeedMetadata(request); });

        return httpResponse;
    }

    protected override Task<UnixTimeUtc> HandleRecoverableTransferStatus(IOdinContext odinContext,
        OdinOutboxProcessingException e)
    {
        var nextRunTime = CalculateNextRunTime(e.TransferStatus);
        return Task.FromResult(nextRunTime);
    }

    protected override Task HandleUnrecoverableTransferStatus(OdinOutboxProcessingException e,
        IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }
}