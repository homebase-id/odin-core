using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.FileUpdate
{
    /// <summary>
    /// Handles the process of updating an existing long-term file based on an incoming temporary file
    /// </summary>
    public class PeerFileUpdateWriter(ILogger logger, FileSystemResolver fileSystemResolver, IDriveManager driveManager)
    {
        public async Task<(bool success, List<PayloadDescriptor> payloads)> UpsertFileAsync(InternalDriveFileId file,
            KeyHeader decryptedKeyHeader,
            OdinId sender,
            EncryptedRecipientFileUpdateInstructionSet instructionSet,
            IOdinContext odinContext,
            WriteSecondDatabaseRowBase markComplete,
            StagingArea sourceArea,
            FileMetadata metadataOverride = null)
        {
            bool success = false;
            List<PayloadDescriptor> payloads = [];
            var fileSystemType = instructionSet.FileSystemType;
            var fs = fileSystemResolver.ResolveFileSystem(fileSystemType);

            logger.LogDebug("PeerFileUpdateWriter - UpsertFileAsync called file: {file}", file);

            var incomingMetadata = await LoadMetadata(file, fs, sourceArea, metadataOverride, odinContext);

            // Validations
            var drive = await driveManager.GetDriveAsync(file.DriveId);
            var isCollaborationChannel = drive.IsCollaborationDrive();
            var targetAcl = await IncomingFileAcl.ForIncomingFileAsync(fileSystemResolver, fileSystemType, incomingMetadata,
                isCollaborationChannel, instructionSet.OriginalAcl, odinContext);

            // A sender's update changes the file the global transit id names, found by the server, not as the sender, who may
            // only write it (docs/file-access.md, "Changing a file you sent")
            var target = instructionSet.Request.File;
            var existingHeader = await fs.Query.GetServerFileHeaderByGlobalTransitIdSkippingFileAcl(target.TargetDrive.Alias,
                target.GlobalTransitId.GetValueOrDefault(), odinContext);

            // On a Private Share drive a member edits a file shared with them, or a comment on one: never a new one.  A
            // standard file keeps its ACL, which an Owner one would close to every member (docs/selective-share-pr3-plan.md,
            // section 7); a comment takes its file's, as on every drive (IncomingFileAcl)
            if (drive.IsSelectiveShare)
            {
                var existingAcl = existingHeader?.ServerMetadata?.AccessControlList ??
                                  throw new OdinSecurityException("No shared file to update");
                if (fileSystemType == FileSystemType.Standard)
                {
                    targetAcl = existingAcl;
                }
            }

            incomingMetadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
            incomingMetadata!.SenderOdinId = sender;

            var request = instructionSet.Request;
            var serverMetadata = new ServerMetadata()
            {
                FileSystemType = fileSystemType,
                AllowDistribution = isCollaborationChannel,
                AccessControlList = targetAcl,
                // Its sender's write, which their next change goes by; the update gives the file the sender's new tag
                // (docs/file-access.md, "Changing a file you sent")
                SendersWrite = SentFileChanges.SendersWriteOf(sender, request.NewVersionTag, request.NewVersionTag)
            };

            if (null == existingHeader)
            {
                logger.LogDebug("PeerFileUpdateWriter WriteNewFile - file: {file}. Payload count: {pc}",
                    file,
                    incomingMetadata.Payloads?.Count);
                //
                // we must create a new file
                //
                // incomingMetadata.VersionTag = instructionSet.Request.NewVersionTag;
                await PerformanceCounter.MeasureExecutionTime("PeerFileUpdateWriter WriteNewFile",
                    async () =>
                    {
                        (success, payloads) = await fs.Storage.CommitNewFile(file,
                            decryptedKeyHeader,
                            incomingMetadata,
                            serverMetadata,
                            ignorePayload: false,
                            odinContext,
                            sourceArea: sourceArea,
                            useThisVersionTag: instructionSet.Request.NewVersionTag,
                            markComplete: markComplete);
                    });

                logger.LogDebug("PeerFileUpdateWriter WriteNewFile - success: {success} committed payload count {pc}",
                    success,
                    payloads?.Count);

                // Return the incoming descriptors (what was staged in inbox temp) so cleanup
                // deletes every staged file, not just whatever the commit happened to return.
                return (success, incomingMetadata.Payloads ?? []);
            }

            var (writeContext, alreadyLanded) = await SentFileChanges.PrepareChangeAsync(fs, drive, existingHeader, sender,
                memberMayUseShares: true, request.NewVersionTag, request.ExpectedVersionTag, odinContext);
            if (alreadyLanded)
            {
                return await SentFileChanges.AlreadyDeliveredAsync(fs, file, incomingMetadata.Payloads, markComplete, odinContext);
            }

            //Update existing file
            await PerformanceCounter.MeasureExecutionTime("PeerFileUpdateWriter UpdateExistingFile", async () =>
            {
                logger.LogDebug("PeerFileUpdateWriter UpdateExistingFile - file: {file}. Payload count: {pc}. GTID: {gtid}",
                    file,
                    incomingMetadata.Payloads?.Count,
                    incomingMetadata.GlobalTransitId);
                
                incomingMetadata.VersionTag = existingHeader.FileMetadata.VersionTag;

                var manifest = new BatchUpdateManifest()
                {
                    NewVersionTag = request.NewVersionTag,
                    PayloadInstruction = request.Manifest.PayloadDescriptors.Select(p => new PayloadInstruction()
                    {
                        Key = p.PayloadKey,
                        OperationType = p.PayloadUpdateOperationType
                    }).ToList(),

                    KeyHeader = decryptedKeyHeader,
                    FileMetadata = incomingMetadata,
                    ServerMetadata = serverMetadata
                };

                (success, payloads) = await fs.Storage.UpdateBatchAsync(file, existingHeader.FileMetadata.File, manifest, writeContext, markComplete, sourceArea: sourceArea);
                
                logger.LogDebug("PeerFileUpdateWriter UpdateExistingFile - success: {success} committed payload count {pc}",
                    success,
                    payloads?.Count);

            });

            // Return the incoming descriptors (what was staged in inbox temp) so cleanup
            // deletes every staged file, not just whatever UpdateBatchAsync committed.
            return (success, incomingMetadata.Payloads ?? []);
        }

        private async Task<FileMetadata> LoadMetadata(
            InternalDriveFileId file,
            IDriveFileSystem fs,
            StagingArea sourceArea,
            FileMetadata metadataOverride,
            IOdinContext odinContext)
        {
            // TODO:INBOX Delete this fallback block (keep only incomingMetadata = metadataOverride) once no
            // pre-upgrade peer-transfer items remain in any inbox.
            // New inbox items carry the metadata on the inbox row (metadataOverride) and staged nothing on disk.
            // Legacy/in-flight items still have a .metadata file in the staging folder; read it (dual-read).
            FileMetadata incomingMetadata = metadataOverride;
            if (incomingMetadata == null)
            {
                var metadataMs = await PerformanceCounter.MeasureExecutionTime("PeerFileUpdateWriter HandleFile ReadTempFile",
                    async () =>
                    {
                        var extension = MultipartHostTransferParts.Metadata.ToString().ToLower();
                        await using var stream = await fs.Storage.OpenTempFileForReadingAsync(file, extension, sourceArea, odinContext);

                        incomingMetadata = await OdinSystemSerializer.Deserialize<FileMetadata>(stream);
                        if (null == incomingMetadata)
                        {
                            logger.LogError("Metadata file (File:{file} on DriveId:{driveID}) could not be deserialized ",
                                file.FileId, file.DriveId);
                            throw new OdinFileWriteException("Metadata could not be deserialized");
                        }
                    });

                logger.LogDebug("PeerFileUpdateWriter - Get metadata from temp file and deserialize: {ms} ms", metadataMs);
            }

            if (incomingMetadata.GlobalTransitId.HasValue == false)
            {
                throw new OdinClientException("Must have a global transit id to write peer.", OdinClientErrorCode.InvalidFile);
            }

            return incomingMetadata;
        }

    }
}