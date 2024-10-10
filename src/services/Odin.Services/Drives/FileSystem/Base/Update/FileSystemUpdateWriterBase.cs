using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Services.Drives.FileSystem.Base.Update;

/// <summary>
/// Enables the update of file streams from external sources and
/// rule enforcement specific to the type of file system
/// </summary>
public abstract class FileSystemUpdateWriterBase
{
    private readonly DriveManager _driveManager;
    private readonly PeerOutgoingTransferService _peerOutgoingTransferService;

    /// <summary />
    protected FileSystemUpdateWriterBase(IDriveFileSystem fileSystem, DriveManager driveManager, PeerOutgoingTransferService peerOutgoingTransferService)
    {
        FileSystem = fileSystem;
        _driveManager = driveManager;
        _peerOutgoingTransferService = peerOutgoingTransferService;
    }

    protected IDriveFileSystem FileSystem { get; }

    internal FileUpdatePackage Package { get; private set; }

    public virtual async Task StartFileUpdate(FileUpdateInstructionSet instructionSet, FileSystemType fileSystemType, IOdinContext odinContext,
        DatabaseConnection cn)
    {
        OdinValidationUtils.AssertNotNull(instructionSet, nameof(instructionSet));
        instructionSet.AssertIsValid();
        OdinValidationUtils.AssertValidRecipientList(instructionSet.Recipients, true, odinContext.Tenant);

        instructionSet.Manifest.ResetPayloadUiDs();

        if (instructionSet.Locale == UpdateLocale.Local)
        {
            throw new NotImplementedException("UpdateLocale.Local will be implemented in api v2");
            //  There must be a local file that will be updated - then sent out to recipients
            //  the outbox is used, and we can write the result to the local file in the transfer history

            // instructionSet.File.AssertIsValid(expectedType: FileIdentifierType.File);
            // var driveId = odinContext.PermissionsContext.GetDriveId(instructionSet.File.TargetDrive);
            //
            // // File to overwrite
            // InternalDriveFileId file = new InternalDriveFileId()
            // {
            //     DriveId = driveId,
            //     FileId = instructionSet.File.FileId.GetValueOrDefault()
            // };
            //
            // this.Package = new FileUpdatePackage(file)
            // {
            //     InstructionSet = instructionSet,
            //     FileSystemType = fileSystemType
            // };
            //
            // await Task.CompletedTask;
            // return;
        }

        if (instructionSet.Locale == UpdateLocale.Peer)
        {
            //Note: there is no local file.  everything is enqueued to the transient temp drive
            OdinValidationUtils.AssertValidRecipientList(instructionSet.Recipients, false, odinContext.Tenant);
            instructionSet.File.AssertIsValid(FileIdentifierType.GlobalTransitId);

            InternalDriveFileId file = new InternalDriveFileId()
            {
                DriveId = (await _driveManager.GetDriveIdByAlias(SystemDriveConstants.TransientTempDrive, cn, true)).GetValueOrDefault(),
                FileId =  Guid.NewGuid() // Note: in the case of peer, there is no local file so we just put a random value in here that will never be used
            };

            this.Package = new FileUpdatePackage(file)
            {
                InstructionSet = instructionSet,
                FileSystemType = fileSystemType
            };

            await Task.CompletedTask;
            return;
        }

        throw new NotImplementedException("Unhandled locale specified");
    }

    public virtual async Task AddMetadata(Stream data, IOdinContext odinContext, DatabaseConnection cn)
    {
        await FileSystem.Storage.WriteTempStream(Package.TempMetadataFile, MultipartUploadParts.Metadata.ToString(), data, odinContext, cn);
    }

    public virtual async Task AddPayload(string key, string contentTypeFromMultipartSection, Stream data, IOdinContext odinContext, DatabaseConnection cn)
    {
        if (Package.Payloads.Any(p => string.Equals(key, p.PayloadKey, StringComparison.InvariantCultureIgnoreCase)))
        {
            throw new OdinClientException($"Duplicate Payload key with key {key} has already been added", OdinClientErrorCode.InvalidUpload);
        }

        var descriptor = Package.InstructionSet.Manifest?.PayloadDescriptors.SingleOrDefault(pd => pd.PayloadKey == key);

        if (null == descriptor)
        {
            throw new OdinClientException($"Cannot find descriptor for payload key {key}", OdinClientErrorCode.InvalidUpload);
        }

        var extension = DriveFileUtility.GetPayloadFileExtension(key, descriptor.PayloadUid);
        var bytesWritten = await FileSystem.Storage.WriteTempStream(Package.InternalFile, extension, data, odinContext, cn);
        if (bytesWritten > 0)
        {
            Package.Payloads.Add(descriptor.PackagePayloadDescriptor(bytesWritten, contentTypeFromMultipartSection));
        }
    }

    public virtual async Task AddThumbnail(string thumbnailUploadKey, string overrideContentType, Stream data, IOdinContext odinContext, DatabaseConnection cn)
    {
        //Note: this assumes you've validated the manifest; so i wont check for duplicates etc

        // if you're adding a thumbnail, there must be a manifest
        var descriptors = Package.InstructionSet.Manifest?.PayloadDescriptors;
        if (null == descriptors)
        {
            throw new OdinClientException("An upload manifest with payload descriptors is required when you're adding thumbnails");
        }

        //find the thumbnail details for the given key
        var result = descriptors.Select(pd =>
        {
            return new
            {
                pd.PayloadKey,
                pd.PayloadUid,
                ThumbnailDescriptor = pd.Thumbnails?.SingleOrDefault(th => th.ThumbnailKey == thumbnailUploadKey)
            };
        }).SingleOrDefault(p => p.ThumbnailDescriptor != null);

        if (null == result)
        {
            throw new OdinClientException(
                $"Error while adding thumbnail; the upload manifest does not " +
                $"have a thumbnail descriptor matching key {thumbnailUploadKey}",
                OdinClientErrorCode.InvalidUpload);
        }

        //TODO: should i validate width and height are > 0?
        string extenstion = DriveFileUtility.GetThumbnailFileExtension(
            result.PayloadKey,
            result.PayloadUid,
            result.ThumbnailDescriptor.PixelWidth,
            result.ThumbnailDescriptor.PixelHeight
        );

        var bytesWritten = await FileSystem.Storage.WriteTempStream(Package.InternalFile, extenstion, data, odinContext, cn);

        Package.Thumbnails.Add(new PackageThumbnailDescriptor()
        {
            PixelHeight = result.ThumbnailDescriptor.PixelHeight,
            PixelWidth = result.ThumbnailDescriptor.PixelWidth,
            ContentType = string.IsNullOrEmpty(result.ThumbnailDescriptor.ContentType?.Trim()) ? overrideContentType : result.ThumbnailDescriptor.ContentType,
            PayloadKey = result.PayloadKey,
            BytesWritten = bytesWritten
        });
    }

    public async Task<FileUpdateResult> FinalizeFileUpdate(IOdinContext odinContext, DatabaseConnection cn)
    {
        var (keyHeaderIv, metadata, serverMetadata) = await UnpackMetadata(Package, odinContext, cn);

        await this.ValidateUploadCore(Package, keyHeaderIv, metadata, serverMetadata, cn);

        if (Package.InstructionSet.Locale == UpdateLocale.Local)
        {
            if (metadata.VersionTag == null)
            {
                throw new OdinClientException("Missing version tag for update operation", OdinClientErrorCode.MissingVersionTag);
            }

            await ProcessExistingFileUpload(Package, keyHeaderIv, metadata, serverMetadata, odinContext, cn);

            var recipientStatus = await ProcessTransitInstructions(Package, keyHeaderIv, odinContext, cn);

            return new FileUpdateResult()
            {
                NewVersionTag = metadata.VersionTag.GetValueOrDefault(),
                RecipientStatus = recipientStatus
            };
        }

        if (Package.InstructionSet.Locale == UpdateLocale.Peer)
        {
            // Note: all changes on remote servers need to use the Package.NewVersionTag
            // There is no local file - everything would be on the temp-transient-drive

            // We send a version tag to other identities so that we can also return it to
            // the local caller since currently, there is no method to get back info from
            // the outbox when sending transient files.

            var keyHeader = new KeyHeader()
            {
                Iv = keyHeaderIv,
                AesKey = Guid.Empty.ToByteArray().ToSensitiveByteArray() // for file updates, we dont touch the key
            };

            await FileSystem.Storage.CommitNewFile(Package.InternalFile, keyHeader, metadata, serverMetadata, false, odinContext, cn);

            if (!serverMetadata.AllowDistribution)
            {
                throw new OdinClientException("AllowDistribution must be true when UpdateLocale is Peer");
            }

            var recipientStatus = await ProcessTransitInstructions(Package, keyHeaderIv, odinContext, cn);
            return new FileUpdateResult()
            {
                NewVersionTag = Package.NewVersionTag,
                RecipientStatus = recipientStatus
            };
        }

        throw new NotImplementedException($"Unhandled UpdateLocale: {Package.InstructionSet.Locale}");
    }

    /// <summary>
    /// Validates the uploaded data is correct before mapping to its final form.
    /// </summary>
    /// <param name="updateDescriptor"></param>
    /// <returns></returns>
    protected abstract Task ValidateUploadDescriptor(UpdateFileDescriptor updateDescriptor);

    /// <summary>
    /// Called when then uploaded file exists on disk.  This is called after core validations are complete
    /// </summary>
    protected virtual async Task ProcessExistingFileUpload(FileUpdatePackage package, byte[] keyHeaderIv, FileMetadata metadata, ServerMetadata serverMetadata,
        IOdinContext odinContext, DatabaseConnection cn)
    {
        var manifest = new BatchUpdateManifest()
        {
            NewVersionTag = Package.NewVersionTag,
            PayloadInstruction = package.Payloads.Select(p => new PayloadInstruction()
            {
                Key = p.PayloadKey,
                OperationType = p.UpdateOperationType
            }).ToList(),

            KeyHeaderIv = keyHeaderIv,
            FileMetadata = metadata,
            ServerMetadata = serverMetadata
        };

        await FileSystem.Storage.UpdateBatch(package.TempMetadataFile, package.InternalFile, manifest, odinContext, cn);
    }

    /// <summary>
    /// Maps the uploaded file to the <see cref="FileMetadata"/> which will be stored on disk,
    /// </summary>
    /// <returns></returns>
    protected abstract Task<FileMetadata> MapUploadToMetadata(FileUpdatePackage package, UpdateFileDescriptor updateDescriptor, IOdinContext odinContext);

    protected virtual async Task<(byte[] keyHeaderIv, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadata(FileUpdatePackage package,
        IOdinContext odinContext, DatabaseConnection cn)
    {
        var clientSharedSecret = odinContext.PermissionsContext.SharedSecretKey;

        var metadataBytes =
            await FileSystem.Storage.GetAllFileBytesFromTemp(package.TempMetadataFile, MultipartUploadParts.Metadata.ToString(), odinContext, cn);
        var decryptedJsonBytes = AesCbc.Decrypt(metadataBytes, clientSharedSecret, package.InstructionSet.TransferIv);
        var updateDescriptor = OdinSystemSerializer.Deserialize<UpdateFileDescriptor>(decryptedJsonBytes.ToStringFromUtf8Bytes());

        byte[] iv = null;

        if (updateDescriptor.FileMetadata.IsEncrypted)
        {
            iv = updateDescriptor!.KeyHeaderIv;
        }

        await ValidateUploadDescriptor(updateDescriptor);

        var metadata = await MapUploadToMetadata(package, updateDescriptor, odinContext);

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = updateDescriptor.FileMetadata.AccessControlList,
            AllowDistribution = updateDescriptor.FileMetadata.AllowDistribution
        };

        return (iv, metadata, serverMetadata);
    }

    protected virtual async Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(FileUpdatePackage package, byte[] keyHeaderIv,
        IOdinContext odinContext,
        DatabaseConnection cn)
    {
        Dictionary<string, TransferStatus> recipientStatus = null;
        var recipients = package.InstructionSet.Recipients;

        OdinValidationUtils.AssertValidRecipientList(recipients, allowEmpty: true);

        if (recipients?.Any() ?? false)
        {
            recipientStatus = await _peerOutgoingTransferService.UpdateFile(
                package.InternalFile,
                keyHeaderIv,
                package.InstructionSet.File,
                package.InstructionSet.Manifest,
                package.InstructionSet.Recipients,
                package.NewVersionTag,
                package.FileSystemType,
                package.InstructionSet.UseAppNotification ? package.InstructionSet.AppNotificationOptions : null,
                package.InstructionSet.Locale,
                odinContext,
                cn);
        }

        return recipientStatus;
    }

    /// <summary>
    /// Validates rules that apply to all files; regardless of being comment, standard, or some other type we've not yet conceived
    /// </summary>
    private async Task ValidateUploadCore(FileUpdatePackage package, byte[] keyHeaderIv, FileMetadata metadata, ServerMetadata serverMetadata,
        DatabaseConnection cn)
    {
        //re-run validation in case we need to verify the instructions are good for encrypted data
        Package.InstructionSet.AssertIsValid(metadata.IsEncrypted);

        if (null == serverMetadata.AccessControlList)
        {
            throw new OdinClientException("Access control list must be specified", OdinClientErrorCode.MissingUploadData);
        }

        serverMetadata.AccessControlList.Validate();

        if (serverMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Anonymous && metadata.IsEncrypted)
        {
            //Note: dont allow anonymously accessible encrypted files because we wont have a client shared secret to secure the key header
            throw new OdinClientException("Cannot upload an encrypted file that is accessible to anonymous visitors",
                OdinClientErrorCode.CannotUploadEncryptedFileForAnonymous);
        }

        if (serverMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Authenticated && metadata.IsEncrypted)
        {
            throw new OdinClientException("Cannot upload an encrypted file that is accessible to authenticated visitors",
                OdinClientErrorCode.CannotUploadEncryptedFileForAnonymous);
        }

        if (metadata.Payloads?.Any() ?? false)
        {
            var hasInvalidPayloads = metadata.Payloads.Any(pd => !pd.IsValid());
            if (hasInvalidPayloads)
            {
                throw new OdinClientException("One or more payload descriptors is invalid", OdinClientErrorCode.InvalidFile);
            }

            if (!metadata.IsEncrypted && Package.GetPayloadsWithValidIVs().Any())
            {
                throw new OdinClientException("All payload IVs must be 0 bytes when server file header is not encrypted", OdinClientErrorCode.InvalidUpload);
            }

            if (metadata.IsEncrypted && !Package.Payloads.All(p => p.HasStrongIv()))
            {
                throw new OdinClientException("When the file is encrypted, you must specify a valid payload IV of 16 bytes", OdinClientErrorCode.InvalidUpload);
            }
        }

        var drive = await _driveManager.GetDrive(package.InternalFile.DriveId, cn, true);
        if (drive.OwnerOnly && serverMetadata.AccessControlList.RequiredSecurityGroup != SecurityGroupType.Owner)
        {
            throw new OdinClientException("Drive is owner only so all files must have RequiredSecurityGroup of Owner",
                OdinClientErrorCode.DriveSecurityAndAclMismatch);
        }

        if (metadata.AppData.UniqueId.HasValue)
        {
            if (metadata.AppData.UniqueId.Value == Guid.Empty)
            {
                throw new OdinClientException("UniqueId cannot be an empty Guid (all zeros)", OdinClientErrorCode.MalformedMetadata);
            }
        }

        if (metadata.IsEncrypted)
        {
            if (ByteArrayUtil.IsStrongKey(keyHeaderIv) == false)
            {
                throw new OdinClientException("Payload is set as encrypted but the encryption key is too simple",
                    code: OdinClientErrorCode.InvalidKeyHeader);
            }
        }
    }

}