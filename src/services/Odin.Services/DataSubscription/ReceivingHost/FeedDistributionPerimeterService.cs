using System;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.DataSubscription.SendingHost;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
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
        DriveManager driveManager)
    {
        public async Task<PeerTransferResponse> AcceptUpdatedFileMetadata(UpdateFeedFileMetadataRequest request, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            await followerService.AssertTenantFollowsTheCaller(odinContext, cn);

            if (request.FileId.TargetDrive != SystemDriveConstants.FeedDrive)
            {
                throw new OdinClientException("Target drive must be the feed drive");
            }

            if (request.FileMetadata.IsEncrypted && request.FeedDistroType == FeedDistroType.CollaborativeChannel)
            {
                return await RouteToInbox(request, odinContext, cn);
            }

            var driveId2 = await driveManager.GetDriveIdByAlias(request.FileId.TargetDrive, cn);
            var drive = await driveManager.GetDrive(driveId2.GetValueOrDefault(), cn);

            Log.Debug(
                "AcceptUpdatedFileMetadata - Caller:{caller} GTID:{gtid} and UID:{uid} on drive {driveName} ({driveId}) - Action: Looking up Internal file",
                odinContext.Caller.OdinId, request.FileId.GlobalTransitId, request.UniqueId, drive.Name, driveId2);

            var newContext = OdinContextUpgrades.UpgradeToReadFollowersForDistribution(odinContext);
            {
                var driveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);

                if (request.FileId.GlobalTransitId == Guid.Empty)
                {
                    Log.Warning("GlobalTransitId not set on incoming feed FileMetadata");
                }

                var fileId = await this.ResolveInternalFile(request.FileId, request.UniqueId, newContext, cn);

                if (null == fileId)
                {
                    Log.Debug(
                        "AcceptUpdatedFileMetadata - Caller:{caller} GTID:{gtid} and UID:{uid} on drive {driveName} ({driveId}) - Action: Creating a new file",
                        odinContext.Caller.OdinId, request.FileId.GlobalTransitId, request.UniqueId, drive, driveId2);

                    //new file
                    var internalFile = await fileSystem.Storage.CreateInternalFileId(driveId, cn);

                    var keyHeader = KeyHeader.Empty();
                    var serverMetadata = new ServerMetadata()
                    {
                        AccessControlList = AccessControlList.OwnerOnly,
                        AllowDistribution = false,
                    };

                    request.FileMetadata.SenderOdinId = odinContext.GetCallerOdinIdOrFail();
                    
                    // Clearing the UID for any files that go into the feed drive because the feed drive 
                    // comes from multiple channel drives from many different identities so there could be a clash
                    request.FileMetadata.AppData.UniqueId = null;
                    
                    var serverFileHeader = await fileSystem.Storage.CreateServerFileHeader(
                        internalFile, keyHeader, request.FileMetadata, serverMetadata, newContext, cn);
                    await fileSystem.Storage.UpdateActiveFileHeader(internalFile, serverFileHeader, odinContext, cn, raiseEvent: true);

                    await mediator.Publish(new NewFeedItemReceived()
                    {
                        Sender = odinContext.GetCallerOdinIdOrFail(),
                        OdinContext = newContext,
                        DatabaseConnection = cn
                    });
                }
                else
                {
                    Log.Debug(
                        "AcceptUpdatedFileMetadata - Caller:{caller} GTID:{gtid} and UID:{uid} on drive {driveName} ({driveId}) - Action: Updating existing file",
                        odinContext.Caller.OdinId, request.FileId.GlobalTransitId, request.UniqueId, drive, driveId2);

                    // perform update
                    try
                    {
                        request.FileMetadata.SenderOdinId = newContext.GetCallerOdinIdOrFail();
                        await fileSystem.Storage.ReplaceFileMetadataOnFeedDrive(fileId.Value, request.FileMetadata, newContext, cn);
                    }
                    catch (Exception e)
                    {
                        Log.Information(e, "AcceptUpdatedFileMetadata - Action: failed while ReplaceFileMetadataOnFeedDrive");
                        throw;
                    }
                }
            }

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<PeerTransferResponse> Delete(DeleteFeedFileMetadataRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            await followerService.AssertTenantFollowsTheCaller(odinContext, cn);
            var newContext = OdinContextUpgrades.UpgradeToReadFollowersForDistribution(odinContext);
            {
                var fileId = await this.ResolveInternalFile(request.FileId, request.UniqueId, newContext, cn);
                if (null == fileId)
                {
                    //TODO: what's the right status code here
                    return new PeerTransferResponse()
                    {
                        Code = PeerResponseCode.AcceptedDirectWrite
                    };
                }

                await fileSystem.Storage.RemoveFeedDriveFile(fileId.Value, newContext, cn);

                return new PeerTransferResponse()
                {
                    Code = PeerResponseCode.AcceptedDirectWrite
                };
            }
        }

        /// <summary>
        /// Looks up a file by a global transit identifier or uniqueId as a fallback
        /// </summary>
        private async Task<InternalDriveFileId?> ResolveInternalFile(GlobalTransitIdFileIdentifier file, Guid? uid, IOdinContext odinContext,
            DatabaseConnection cn)
        {
            Log.Debug("FeedDistributionPerimeterService - looking up fileId by global transit id");
            var (fs, fileId) = await fileSystemResolver.ResolveFileSystem(file, odinContext, cn, tryCommentDrive: false);

            if (fileId == null)
            {
                //look it up by uniqueId
                Log.Debug("FeedDistributionPerimeterService - failed to lookup file by globalTransitId; now trying the uid");

                try
                {
                    Log.Debug("Seeking the global transit id: {gtid} (as hex x'{hex}')", file.GlobalTransitId,
                        Convert.ToHexString(file.GlobalTransitId.ToByteArray()));
                    var allDrives = await driveManager.GetDrives(PageOptions.All, odinContext, cn);
                    var dump = await fs.Query.DumpGlobalTransitId(allDrives.Results.ToList(), file.GlobalTransitId, odinContext, cn);

                    foreach (var result in dump.Results)
                    {
                        Log.Information("Results for {name}", result.Name);
                        foreach (var sr in result.SearchResults)
                        {
                            Log.Information("FileId:{fileId}\tSender: {sender}\tuniqueId:{uniqueId}\tstate:{fs}",
                                sr.FileId,
                                sr.FileMetadata.SenderOdinId,
                                sr.FileMetadata.AppData.UniqueId,
                                sr.FileState);
                        }
                    }
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
                        var allDrives = await driveManager.GetDrives(PageOptions.All, odinContext, cn);
                        var dump = await fs.Query.DumpUniqueId(allDrives.Results.ToList(), uid.GetValueOrDefault(), odinContext, cn);

                        foreach (var result in dump.Results)
                        {
                            Log.Information("Results for {name}", result.Name);
                            foreach (var f in result.FileIdList)
                            {
                                Log.Information("FileId:{fileId}", f);
                            }
                        }
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
                    //     var fileByClientUniqueId = await fs.Query.GetFileByClientUniqueId(driveId, uid.GetValueOrDefault(), odinContext, cn);
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

        private async Task<PeerTransferResponse> RouteToInbox(UpdateFeedFileMetadataRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            var feedDriveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);

            // Write to temp file
            var file = await fileSystem.Storage.CreateInternalFileId(feedDriveId, cn);
            var stream = OdinSystemSerializer.Serialize(request.FileMetadata).ToUtf8ByteArray().ToMemoryStream();
            await fileSystem.Storage.WriteTempStream(file, MultipartHostTransferParts.Metadata.ToString().ToLower(), stream, odinContext, cn);
            await stream.DisposeAsync();

            // then tell the inbox you have a new file
            var item = new TransferInboxItem
            {
                Id = Guid.NewGuid(),
                AddedTimestamp = UnixTimeUtc.Now(),
                Sender = odinContext.GetCallerOdinIdOrFail(),
                InstructionType = TransferInstructionType.SaveFile,

                FileId = file.FileId,
                DriveId = file.DriveId,
                GlobalTransitId = request.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                FileSystemType = FileSystemType.Standard, //comments are never distributed

                Priority = 0,
                Marker = default,
                TransferFileType = TransferFileType.EncryptedFileForFeed,

                TransferInstructionSet = new EncryptedRecipientTransferInstructionSet()
                {
                    FileSystemType = FileSystemType.Standard,
                    TransferFileType = TransferFileType.EncryptedFileForFeed,
                    ContentsProvided = SendContents.Header
                },

                //Feed stuff
                EncryptedFeedPayload = request.EncryptedPayload
            };

            //write the file to disk
            await inboxBoxStorage.Add(item, cn);

            await mediator.Publish(new TransitFileReceivedNotification()
            {
                TempFile = new ExternalFileIdentifier()
                {
                    TargetDrive = SystemDriveConstants.FeedDrive,
                    FileId = item.FileId
                },

                TransferFileType = item.TransferInstructionSet.TransferFileType,
                FileSystemType = item.TransferInstructionSet.FileSystemType,
                OdinContext = odinContext,
                DatabaseConnection = cn
            });

            return new PeerTransferResponse
            {
                Code = PeerResponseCode.AcceptedIntoInbox,
                Message = null
            };
        }
    }
}