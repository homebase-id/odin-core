using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.DataSubscription;
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
    public class PeerFileUpdateWriter(ILogger logger, FileSystemResolver fileSystemResolver, DriveManager driveManager)
    {
        public async Task UpdateFile(InternalDriveFileId tempFile,
            KeyHeader decryptedKeyHeader,
            OdinId sender,
            EncryptedRecipientFileUpdateInstructionSet instructionSet,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            var fileSystemType = instructionSet.FileSystemType;
            var fs = fileSystemResolver.ResolveFileSystem(fileSystemType);
            var incomingMetadata = await LoadMetadataFromTemp(tempFile, fs, odinContext, cn);

            // Validations
            var (targetFile, existingHeader) = await GetTargetFileHeader(instructionSet.Request.File, fs, odinContext, cn);
            existingHeader.AssertOriginalSender((OdinId)existingHeader.FileMetadata.SenderOdinId);

            var (targetAcl, isCollabChannel) = await DetermineAcl(tempFile, instructionSet, fileSystemType, incomingMetadata, odinContext, cn);

            var serverMetadata = new ServerMetadata()
            {
                FileSystemType = fileSystemType,
                AllowDistribution = isCollabChannel,
                AccessControlList = targetAcl
            };

            incomingMetadata!.SenderOdinId = sender;

            incomingMetadata.VersionTag = existingHeader.FileMetadata.VersionTag;

            //Update existing file

            await PerformanceCounter.MeasureExecutionTime("PeerFileUpdateWriter UpdateExistingFile", async () =>
            {
                //Use the version tag from the recipient's server because it won't match the sender (this is due to the fact a new
                //one is written any time you save a header)
                incomingMetadata.TransitUpdated = UnixTimeUtc.Now().milliseconds;
                
                var request = instructionSet.Request;
                var manifest = new BatchUpdateManifest()
                {
                    NewVersionTag = request.NewVersionTag,
                    PayloadInstruction = request.Manifest.PayloadDescriptors.Select(p => new PayloadInstruction()
                    {
                        Key = p.PayloadKey,
                        OperationType = p.PayloadUpdateOperationType
                    }).ToList(),

                    KeyHeaderIv = decryptedKeyHeader.Iv,
                    FileMetadata = incomingMetadata,
                    ServerMetadata = serverMetadata
                };

                await fs.Storage.UpdateBatch(targetFile, manifest, odinContext, cn);
            });
        }

        private async Task<FileMetadata> LoadMetadataFromTemp(
            InternalDriveFileId tempFile,
            IDriveFileSystem fs,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            FileMetadata incomingMetadata = default;
            var metadataMs = await PerformanceCounter.MeasureExecutionTime("PeerFileUpdateWriter HandleFile ReadTempFile", async () =>
            {
                var bytes = await fs.Storage.GetAllFileBytesForWriting(tempFile, MultipartHostTransferParts.Metadata.ToString().ToLower(), odinContext, cn);

                if (bytes == null)
                {
                    // this is bad error.
                    logger.LogError("Cannot find the metadata file (File:{file} on DriveId:{driveID}) was not found ", tempFile.FileId, tempFile.DriveId);
                    throw new OdinFileWriteException("Missing temp file while processing inbox");
                }

                string json = bytes.ToStringFromUtf8Bytes();

                incomingMetadata = OdinSystemSerializer.Deserialize<FileMetadata>(json);
                if (null == incomingMetadata)
                {
                    logger.LogError("Metadata file (File:{file} on DriveId:{driveID}) could not be deserialized ", tempFile.FileId, tempFile.DriveId);
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

        private async Task<(AccessControlList acl, bool isCollabChannel)> DetermineAcl(InternalDriveFileId tempFile,
            EncryptedRecipientFileUpdateInstructionSet instructionSet,
            FileSystemType fileSystemType,
            FileMetadata metadata,
            IOdinContext odinContext,
            DatabaseConnection cn)
        {
            // Files coming from other systems are only accessible to the owner so
            // the owner can use the UI to pass the file along
            var targetAcl = new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Owner
            };

            var drive = await driveManager.GetDrive(tempFile.DriveId, cn);
            var isCollabChannel = drive.Attributes.TryGetValue(FeedDriveDistributionRouter.IsCollaborativeChannel, out string value)
                                  && bool.TryParse(value, out bool collabChannelFlagValue)
                                  && collabChannelFlagValue;

            //TODO: this might be a hacky place to put this but let's let it cook.  It might better be put into the comment storage
            if (fileSystemType == FileSystemType.Comment)
            {
                targetAcl = await ResetAclForComment(metadata, odinContext, cn);
            }
            else
            {
                //
                // Collab channel hack; need to cleanup location of the IsCollaborativeChannel flag
                //
                if (isCollabChannel)
                {
                    targetAcl = instructionSet.OriginalAcl ?? new AccessControlList()
                    {
                        RequiredSecurityGroup = SecurityGroupType.Owner
                    };
                }
            }

            return (targetAcl, isCollabChannel);
        }


        private async Task<(InternalDriveFileId targetFile, SharedSecretEncryptedFileHeader targetHeader)> GetTargetFileHeader(FileIdentifier file,
            IDriveFileSystem fs,
            IOdinContext odinContext, DatabaseConnection cn)
        {
            var targetDriveId = odinContext.PermissionsContext.GetDriveId(file.TargetDrive);

            SharedSecretEncryptedFileHeader header =
                await fs.Query.GetFileByGlobalTransitId(targetDriveId, file.GlobalTransitId.GetValueOrDefault(), odinContext, cn);

            if (header == null)
            {
                throw new OdinClientException("File does not exist", OdinClientErrorCode.InvalidFile);
            }

            header.AssertFileIsActive();
            var targetFile = new InternalDriveFileId
            {
                DriveId = targetDriveId,
                FileId = header.FileId
            };

            return (targetFile, header);
        }

        private async Task<AccessControlList> ResetAclForComment(FileMetadata metadata, IOdinContext odinContext, DatabaseConnection cn)
        {
            AccessControlList targetAcl;

            var (referencedFs, fileId) = await fileSystemResolver.ResolveFileSystem(metadata.ReferencedFile, odinContext, cn);

            if (null == referencedFs || !fileId.HasValue)
            {
                throw new OdinClientException("Referenced file missing or caller does not have access");
            }

            //
            // Issue - the caller cannot see the ACL because it's only shown to the
            // owner, so we need to forceIncludeServerMetadata
            //

            var referencedFile = await referencedFs.Query.GetFileByGlobalTransitId(fileId.Value.DriveId,
                metadata.ReferencedFile.GlobalTransitId, odinContext: odinContext, forceIncludeServerMetadata: true, cn: cn);

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