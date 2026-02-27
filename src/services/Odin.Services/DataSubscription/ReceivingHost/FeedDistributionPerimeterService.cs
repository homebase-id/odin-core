using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.DataSubscription.SendingHost;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;
using Serilog;

namespace Odin.Services.DataSubscription.ReceivingHost
{
    public class FeedDistributionPerimeterService(
        IDriveFileSystem fileSystem,
        FileSystemResolver fileSystemResolver,
        FollowerService followerService,
        IMediator mediator,
        TransitInboxBoxStorage inboxBoxStorage,
        IDriveManager driveManager,
        ILogger<FeedDistributionPerimeterService> logger,
        FeedWriter feedWriter)
    {
        public async Task<PeerTransferResponse> AcceptUpdatedFileMetadataAsync(UpdateFeedFileMetadataRequest request,
            IOdinContext odinContext)
        {
            logger.LogDebug("AcceptUpdatedFileMetadata called");
            await followerService.AssertTenantFollowsTheCallerAsync(odinContext);
            var sender = odinContext.GetCallerOdinIdOrFail();

            if (request.FeedDistroType == FeedDistroType.CollaborativeChannel)
            {
                if (request.FileMetadata.IsEncrypted)
                {
                    return await RouteFeedRequestToInboxAsync(request, odinContext);
                }
            }

            var driveId2 = request.FileId.TargetDrive.Alias;
            var drive = await driveManager.GetDriveAsync(driveId2);

            Log.Debug(
                "AcceptUpdatedFileMetadata - Caller:{caller} GTID:{gtid} and UID:{uid} " +
                "on drive {driveName} ({driveId}) - Action: Looking up Internal file",
                odinContext.Caller.OdinId, request.FileId.GlobalTransitId, request.UniqueId, drive.Name, driveId2);

            var newContext = OdinContextUpgrades.UpgradeToReadFollowersForDistribution(odinContext);
            {
                if (request.FileId.GlobalTransitId == Guid.Empty)
                {
                    Log.Warning("GlobalTransitId not set on incoming feed FileMetadata");
                }

                var file = await this.ResolveInternalFileAsync(request.FileId, request.UniqueId, newContext);

                if (null == file)
                {
                    //
                    // Create a new file on the feed
                    //
                    Log.Debug(
                        "AcceptUpdatedFileMetadata - Caller:{caller} GTID:{gtid} and UID:{uid} " +
                        "on drive {driveName} ({driveId}) - Action: Creating a new file",
                        odinContext.Caller.OdinId, request.FileId.GlobalTransitId, request.UniqueId, drive, driveId2);

                    var keyHeader = KeyHeader.Empty();
                    var fileMetadata = request.FileMetadata;

                    fileMetadata.SenderOdinId = sender;

                    await feedWriter.WriteNewFileToFeedDriveAsync(keyHeader, fileMetadata, odinContext);

                    await mediator.Publish(new NewFeedItemReceived()
                    {
                        Sender = sender,
                        OdinContext = newContext,
                        GlobalTransitId = request.FileMetadata.ReferencedFile != null
                            ? request.FileMetadata.ReferencedFile.GlobalTransitId
                            : request.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                    });
                }
                else
                {
                    Log.Debug(
                        "AcceptUpdatedFileMetadata - Caller:{caller} GTID:{gtid} and UID:{uid} on drive {driveName} ({driveId}) - Action: Updating existing file",
                        odinContext.Caller.OdinId, request.FileId.GlobalTransitId, request.UniqueId, drive, driveId2);

                    // perform update
                    request.FileMetadata.SenderOdinId = sender;
                    await feedWriter.ReplaceFileMetadataOnFeedDrive(file.Value.FileId, request.FileMetadata, newContext,
                        bypassCallerCheck: true);
                }
            }

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<PeerTransferResponse> DeleteAsync(DeleteFeedFileMetadataRequest request, IOdinContext odinContext)
        {
            await followerService.AssertTenantFollowsTheCallerAsync(odinContext);
            var newContext = OdinContextUpgrades.UpgradeToReadFollowersForDistribution(odinContext);
            {
                var fileId = await this.ResolveInternalFileAsync(request.FileId, request.UniqueId, newContext);
                if (null == fileId)
                {
                    //TODO: what's the right status code here
                    return new PeerTransferResponse()
                    {
                        Code = PeerResponseCode.AcceptedDirectWrite
                    };
                }

                await feedWriter.RemoveFeedDriveFile(fileId.Value, newContext);

                return new PeerTransferResponse()
                {
                    Code = PeerResponseCode.AcceptedDirectWrite
                };
            }
        }

        /// <summary>
        /// Looks up a file by a global transit identifier or uniqueId as a fallback
        /// </summary>
        private async Task<InternalDriveFileId?> ResolveInternalFileAsync(GlobalTransitIdFileIdentifier file, Guid? uid,
            IOdinContext odinContext)
        {
            Log.Debug("FeedDistributionPerimeterService - looking up fileId by global transit id");
            var (fs, fileId) = await fileSystemResolver.ResolveFileSystem(file, odinContext, tryCommentDrive: false);

            if (fileId == null)
            {
                //look it up by uniqueId
                Log.Debug("FeedDistributionPerimeterService - failed to lookup file by globalTransitId; now trying the uid");

                try
                {
                    Log.Debug("Seeking the global transit id: {gtid} (as hex x'{hex}')", file.GlobalTransitId,
                        Convert.ToHexString(file.GlobalTransitId.ToByteArray()));
                }
                catch (Exception e)
                {
                    Log.Debug(e, "Failed dumping info by global transitId");
                }

                if (uid.HasValue)
                {
                    try
                    {
                        Log.Debug("Seeking the uniqueId id: {uid} (as hex x'{hex}')", uid.GetValueOrDefault(),
                            Convert.ToHexString(uid.GetValueOrDefault().ToByteArray()));
                    }
                    catch (Exception e)
                    {
                        Log.Debug(e, "Failed dumping info by uniqueId");
                    }

                    // Guid driveId;
                    // try
                    // {
                    //     driveId = odinContext.PermissionsContext.GetDriveId(file.TargetDrive);
                    // }
                    // catch (Exception e)
                    // {
                    //     Log.Debug(e, "FeedDistributionPerimeterService - Failed getting driveId for target drive {targetDrive} from permission context",
                    //         file.TargetDrive);
                    //     throw;
                    // }

                    // try
                    // {
                    //     var fileByClientUniqueId = await fs.Query.GetFileByClientUniqueId(driveId, uid.GetValueOrDefault(), odinContext);
                    //     if (fileByClientUniqueId == null)
                    //     {
                    //         Log.Debug("FeedDistributionPerimeterService - file not found by uniqueId [{uid}]", uid);
                    //         return null;
                    //     }
                    //
                    //     Log.Debug("FeedDistributionPerimeterService - file found by uniqueId");
                    //     return new InternalDriveFileId()
                    //     {
                    //         FileId = fileByClientUniqueId.FileId,
                    //         DriveId = driveId
                    //     };
                    // }
                    // catch (Exception e)
                    // {
                    //     Log.Debug(e, "FeedDistributionPerimeterService - Failed while looking file by UID");
                    //     throw;
                    // }
                }
            }

            Log.Debug("FeedDistributionPerimeterService - Found file {fileId} by GTID {gtid} on feedDrive", fileId, file.GlobalTransitId);
            return fileId;
        }

        private async Task<PeerTransferResponse> RouteFeedRequestToInboxAsync(UpdateFeedFileMetadataRequest request,
            IOdinContext odinContext)
        {
            try
            {
                logger.LogDebug("RouteFeedRequestToInbox for gtid: {gtid}", request.FileId.GlobalTransitId);
                var feedDriveId = SystemDriveConstants.FeedDrive.Alias;
                logger.LogDebug("Found feed drive id {id}", feedDriveId);

                // Write to temp file
                var tempFile = new InboxFile(await fileSystem.Storage.CreateInternalFileId(feedDriveId, odinContext));

                var stream = OdinSystemSerializer.Serialize(request.FileMetadata).ToUtf8ByteArray().ToMemoryStream();
                await fileSystem.Storage.WriteInboxStream(tempFile, MultipartHostTransferParts.Metadata.ToString().ToLower(), stream,
                    odinContext);
                await stream.DisposeAsync();

                // then tell the inbox you have a new file
                var item = new TransferInboxItem
                {
                    Id = Guid.NewGuid(),
                    AddedTimestamp = UnixTimeUtc.Now(),
                    Sender = odinContext.GetCallerOdinIdOrFail(),
                    InstructionType = TransferInstructionType.SaveFile,

                    FileId = tempFile.FileId.FileId,
                    DriveId = tempFile.FileId.DriveId,
                    GlobalTransitId = request.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                    FileSystemType = FileSystemType.Standard, //comments are never distributed

                    Priority = 200,
                    Marker = default,
                    TransferFileType = TransferFileType.EncryptedFileForFeed,

                    TransferInstructionSet = new EncryptedRecipientTransferInstructionSet()
                    {
                        FileSystemType = FileSystemType.Standard,
                        TransferFileType = TransferFileType.EncryptedFileForFeed
                    },

                    //Feed stuff
                    EncryptedFeedPayload = request.EncryptedPayload
                };

                await inboxBoxStorage.AddAsync(item);

                await mediator.Publish(new InboxItemReceivedNotification()
                {
                    TargetDrive = SystemDriveConstants.FeedDrive,
                    TransferFileType = item.TransferInstructionSet.TransferFileType,
                    FileSystemType = item.TransferInstructionSet.FileSystemType,
                });

                return new PeerTransferResponse
                {
                    Code = PeerResponseCode.AcceptedIntoInbox
                };
            }
            catch (Exception e)
            {
                logger.LogError(e, "this sucker is logged!");
                throw;
            }
        }
    }
}