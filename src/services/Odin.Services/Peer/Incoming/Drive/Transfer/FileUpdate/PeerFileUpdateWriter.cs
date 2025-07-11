using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
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
        public async Task<(bool success, List<PayloadDescriptor> payloads)> UpsertFileAsync(TempFile tempFile,
            KeyHeader decryptedKeyHeader,
            OdinId sender,
            EncryptedRecipientFileUpdateInstructionSet instructionSet,
            IOdinContext odinContext, 
            WriteSecondDatabaseRowBase markComplete)
        {
            bool success = false;
            List<PayloadDescriptor> payloads = [];
            var fileSystemType = instructionSet.FileSystemType;
            var fs = fileSystemResolver.ResolveFileSystem(fileSystemType);
            var incomingMetadata = await LoadMetadataFromTemp(tempFile, fs, odinContext);

            // Validations
            var (targetFile, existingHeader) = await GetTargetFileHeader(instructionSet.Request.File, fs, odinContext);
            var (targetAcl, isCollaborationChannel) = await DetermineAclAsync(tempFile,
                instructionSet,
                fileSystemType,
                incomingMetadata,
                odinContext);

            incomingMetadata.TransitCreated = UnixTimeUtc.Now().milliseconds;
            incomingMetadata!.SenderOdinId = sender;

            var serverMetadata = new ServerMetadata()
            {
                FileSystemType = fileSystemType,
                AllowDistribution = isCollaborationChannel,
                AccessControlList = targetAcl
            };

            if (null == existingHeader)
            {
                //
                // we must create a new file
                //
                // incomingMetadata.VersionTag = instructionSet.Request.NewVersionTag;
                await PerformanceCounter.MeasureExecutionTime("PeerFileUpdateWriter WriteNewFile",
                    async () =>
                    {
                        (success, payloads) = await fs.Storage.CommitNewFile(tempFile,
                            decryptedKeyHeader,
                            incomingMetadata,
                            serverMetadata,
                            ignorePayload: false,
                            odinContext,
                            useThisVersionTag: instructionSet.Request.NewVersionTag);
                    });


                return (success, payloads);
            }

            //Update existing file
            await PerformanceCounter.MeasureExecutionTime("PeerFileUpdateWriter UpdateExistingFile", async () =>
            {
                if (!isCollaborationChannel)
                {
                    existingHeader.AssertOriginalSender((OdinId)existingHeader.FileMetadata.SenderOdinId);
                }

                //Use the version tag from the recipient's server because it won't match the sender
                //(this is due to the fact a new one is written any time you save a header)
                incomingMetadata.VersionTag = existingHeader.FileMetadata.VersionTag;

                var request = instructionSet.Request;
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

                (success, payloads) = await fs.Storage.UpdateBatchAsync(tempFile, targetFile.Value, manifest, odinContext, markComplete);
            });

            return (success, payloads);
        }

        private async Task<FileMetadata> LoadMetadataFromTemp(
            TempFile tempFile,
            IDriveFileSystem fs,
            IOdinContext odinContext)
        {
            FileMetadata incomingMetadata = default;
            var metadataMs = await PerformanceCounter.MeasureExecutionTime("PeerFileUpdateWriter HandleFile ReadTempFile", async () =>
            {
                var bytes = await fs.Storage.GetAllFileBytesFromTempFile(tempFile, MultipartHostTransferParts.Metadata.ToString().ToLower(),
                    odinContext);

                if (bytes == null)
                {
                    // this is bad error.
                    logger.LogError("Cannot find the metadata file (File:{file} on DriveId:{driveID}) was not found ", tempFile.File.FileId,
                        tempFile.File.DriveId);
                    throw new OdinFileWriteException("Missing temp file while processing inbox");
                }

                string json = bytes.ToStringFromUtf8Bytes();

                incomingMetadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);
                if (null == incomingMetadata)
                {
                    logger.LogError("Metadata file (File:{file} on DriveId:{driveID}) could not be deserialized ", tempFile.File.FileId,
                        tempFile.File.DriveId);
                    throw new OdinFileWriteException("Metadata could not be deserialized");
                }
            });

            logger.LogDebug("PeerFileUpdateWriter - Get metadata from temp file and deserialize: {ms} ms", metadataMs);

            if (incomingMetadata.GlobalTransitId.HasValue == false)
            {
                throw new OdinClientException("Must have a global transit id to write peer.", OdinClientErrorCode.InvalidFile);
            }

            return incomingMetadata;
        }

        private async Task<(AccessControlList acl, bool isCollabChannel)> DetermineAclAsync(TempFile tempFile,
            EncryptedRecipientFileUpdateInstructionSet instructionSet,
            FileSystemType fileSystemType,
            FileMetadata metadata,
            IOdinContext odinContext)
        {
            // Files coming from other systems are only accessible to the owner so
            // the owner can use the UI to pass the file along
            var targetAcl = new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Owner
            };

            var drive = await driveManager.GetDriveAsync(tempFile.File.DriveId);
            var isCollaborationChannel = drive.IsCollaborationDrive();

            //TODO: this might be a hacky place to put this but let's let it cook.  It might better be put into the comment storage
            if (fileSystemType == FileSystemType.Comment)
            {
                targetAcl = await ResetAclForComment(metadata, odinContext);
            }
            else
            {
                //
                // Collab channel hack; need to cleanup location of the IsCollaborativeChannel flag
                //
                if (isCollaborationChannel)
                {
                    targetAcl = instructionSet.OriginalAcl ?? new AccessControlList()
                    {
                        RequiredSecurityGroup = SecurityGroupType.Owner
                    };
                }
            }

            return (targetAcl, isCollaborationChannel);
        }

        private async Task<(InternalDriveFileId? targetFile, SharedSecretEncryptedFileHeader targetHeader)> GetTargetFileHeader(
            FileIdentifier file,
            IDriveFileSystem fs,
            IOdinContext odinContext)
        {
            var targetDriveId = file.TargetDrive.Alias;

            SharedSecretEncryptedFileHeader header =
                await fs.Query.GetFileByGlobalTransitId(targetDriveId, file.GlobalTransitId.GetValueOrDefault(), odinContext);

            if (header == null)
            {
                return (null, null);
                // throw new OdinClientException("File does not exist", OdinClientErrorCode.InvalidFile);
            }

            header.AssertFileIsActive();
            var targetFile = new InternalDriveFileId
            {
                DriveId = targetDriveId,
                FileId = header.FileId
            };

            return (targetFile, header);
        }

        private async Task<AccessControlList> ResetAclForComment(FileMetadata metadata, IOdinContext odinContext)
        {
            AccessControlList targetAcl;

            var (referencedFs, fileId) = await fileSystemResolver.ResolveFileSystem(metadata.ReferencedFile, odinContext);

            if (null == referencedFs || !fileId.HasValue)
            {
                throw new OdinClientException("Referenced file missing or caller does not have access");
            }

            //
            // Issue - the caller cannot see the ACL because it's only shown to the
            // owner, so we need to forceIncludeServerMetadata
            //

            var referencedFile = await referencedFs.Query.GetFileByGlobalTransitId(fileId.Value.DriveId,
                metadata.ReferencedFile.GlobalTransitId, odinContext: odinContext, forceIncludeServerMetadata: true);

            if (null == referencedFile)
            {
                //TODO file does not exist or some other issue - need clarity on what is happening here
                throw new OdinRemoteIdentityException("Referenced file missing or caller does not have access");
            }


            //S2040
            if (referencedFile.FileMetadata.IsEncrypted != metadata.IsEncrypted)
            {
                throw new OdinRemoteIdentityException("Referenced filed and metadata payload encryption do not match");
            }

            targetAcl = referencedFile.ServerMetadata.AccessControlList;

            return targetAcl;
        }
    }
}