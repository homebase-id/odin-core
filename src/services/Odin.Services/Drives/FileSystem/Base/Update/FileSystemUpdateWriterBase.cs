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
using Odin.Core.Time;
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
    private readonly IPeerOutgoingTransferService _peerOutgoingTransferService;

    /// <summary />
    protected FileSystemUpdateWriterBase(IDriveFileSystem fileSystem, DriveManager driveManager, IPeerOutgoingTransferService peerOutgoingTransferService)
    {
        FileSystem = fileSystem;
        _driveManager = driveManager;
        _peerOutgoingTransferService = peerOutgoingTransferService;
    }

    protected IDriveFileSystem FileSystem { get; }

    public FileUpdatePackage Package { get; private set; }

    public virtual async Task StartFileUpdate(FileUpdateInstructionSet instructionSet, IOdinContext odinContext, DatabaseConnection cn)
    {
        OdinValidationUtils.AssertNotNull(instructionSet, nameof(instructionSet));
        instructionSet.AssertIsValid();
        OdinValidationUtils.AssertValidRecipientList(instructionSet.Recipients, true, odinContext.Tenant);

        instructionSet.Manifest.ResetPayloadUiDs();

        if (instructionSet.Locale == UpdateLocale.Local)
        {
            //  there must be a local file that will be updated - then sent out to recipients
            //  the outbox is used and we can write the result to the local file in the transfer history

            instructionSet.File.AssertIsValid(FileIdentifierType.File);
            var driveId = odinContext.PermissionsContext.GetDriveId(instructionSet.File.TargetDrive);

            // File to overwrite
            InternalDriveFileId file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = instructionSet.File.FileId.GetValueOrDefault()
            };

            this.Package = new FileUpdatePackage(file, instructionSet);
            await Task.CompletedTask;
            return;
        }

        if (instructionSet.Locale == UpdateLocale.Peer)
        {
            //Note: there is no local file.  everything is enqueued to the transient temp drive

            instructionSet.File.AssertIsValid(FileIdentifierType.GlobalTransitId);
            InternalDriveFileId file = new InternalDriveFileId()
            {
                DriveId = (await _driveManager.GetDriveIdByAlias(SystemDriveConstants.TransientTempDrive, cn, true)).GetValueOrDefault(),
                FileId = instructionSet.File.FileId.GetValueOrDefault()
            };

            this.Package = new FileUpdatePackage(file, instructionSet);

            // The outbox is used but there is no place to write the result
            await Task.CompletedTask;
            return;
        }

        throw new NotImplementedException("Unhandled locale specified");
    }

    public virtual async Task AddMetadata(Stream data, IOdinContext odinContext, DatabaseConnection cn)
    {
        await FileSystem.Storage.WriteTempStream(Package.TempMetadataFile, MultipartUploadParts.Metadata.ToString(), data, odinContext, cn);
    }

    public virtual async Task AddPayload(string key, string overrideContentType, Stream data, IOdinContext odinContext, DatabaseConnection cn)
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
            Package.Payloads.Add(new PackagePayloadDescriptor()
            {
                Iv = descriptor.Iv,
                Uid = descriptor.PayloadUid,
                PayloadKey = key,
                ContentType = string.IsNullOrEmpty(descriptor.ContentType?.Trim()) ? overrideContentType : descriptor.ContentType,
                LastModified = UnixTimeUtc.Now(),
                BytesWritten = bytesWritten,
                DescriptorContent = descriptor.DescriptorContent,
                PreviewThumbnail = descriptor.PreviewThumbnail
            });
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

    public async Task<FileUpdateResult> Finalize(IOdinContext odinContext, DatabaseConnection cn)
    {
        var (keyHeader, metadata, serverMetadata) = await UnpackMetadata(Package, odinContext, cn);

        await this.ValidateUploadCore(Package, keyHeader, metadata, serverMetadata, cn);
        
        //
        // TODO: Need to send a version tag along to remote identities so they will use it.
        //

        if (Package.InstructionSet.Locale ==)
        {
            if (metadata.VersionTag == null)
            {
                throw new OdinClientException("Missing version tag for update operation", OdinClientErrorCode.MissingVersionTag);
            }

            await ProcessExistingFileUpload(Package, keyHeader, metadata, serverMetadata, odinContext, cn);
        }
        else
        {
            await ProcessNewFileUpload(Package, keyHeader, metadata, serverMetadata, odinContext, cn);
        }

        Dictionary<string, TransferStatus> recipientStatus = await ProcessTransitInstructions(Package, odinContext, cn);

        var uploadResult = new FileUpdateResult()
        {
            NewVersionTag = Package.TargetVersionTag ?? //metadata.VersionTag.GetValueOrDefault(),
                RecipientStatus = recipientStatus
        };


        return uploadResult;
    }

    /// <summary>
    /// Validates the uploaded data is correct before mapping to its final form.
    /// </summary>
    /// <param name="uploadDescriptor"></param>
    /// <returns></returns>
    protected abstract Task ValidateUploadDescriptor(UploadFileDescriptor uploadDescriptor);
    
    /// <summary>
    /// Called when the incoming file does not exist on disk.  This is called after core validations are complete
    /// </summary>
    protected abstract Task ProcessNewFileUpload(FileUpdatePackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata,
        IOdinContext odinContext, DatabaseConnection cn);

    /// <summary>
    /// Called when then uploaded file exists on disk.  This is called after core validations are complete
    /// </summary>
    protected abstract Task ProcessExistingFileUpload(FileUpdatePackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata,
        IOdinContext odinContext, DatabaseConnection cn);

    /// <summary>
    /// Called after the file is uploaded to process how transit will deal w/ the instructions
    /// </summary>
    /// <returns></returns>
    protected abstract Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(FileUpdatePackage package, IOdinContext odinContext,
        DatabaseConnection cn);

    /// <summary>
    /// Maps the uploaded file to the <see cref="FileMetadata"/> which will be stored on disk,
    /// </summary>
    /// <returns></returns>
    protected abstract Task<FileMetadata> MapUploadToMetadata(FileUpdatePackage package, UploadFileDescriptor uploadDescriptor, IOdinContext odinContext);

    protected virtual async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadata(FileUpdatePackage package,
        IOdinContext odinContext, DatabaseConnection cn)
    {
        var clientSharedSecret = odinContext.PermissionsContext.SharedSecretKey;

        var metadataBytes = await FileSystem.Storage.GetAllFileBytes(package.TempMetadataFile, MultipartUploadParts.Metadata.ToString(), odinContext, cn);
        var decryptedJsonBytes = AesCbc.Decrypt(metadataBytes, clientSharedSecret, package.InstructionSet.TransferIv);
        var uploadDescriptor = OdinSystemSerializer.Deserialize<UploadFileDescriptor>(decryptedJsonBytes.ToStringFromUtf8Bytes());
        return await UnpackMetadataForNewFileOrOverwrite(package, uploadDescriptor, odinContext);
    }

    protected async Task<Dictionary<string, TransferStatus>> ProcessTransitBasic(FileUpdatePackage package, FileSystemType fileSystemType,
        IOdinContext odinContext, DatabaseConnection cn)
    {
        Dictionary<string, TransferStatus> recipientStatus = null;
        var recipients = package.InstructionSet.Recipients;

        OdinValidationUtils.AssertValidRecipientList(recipients, allowEmpty: true);

        if (recipients?.Any() ?? false)
        {
            recipientStatus = await _peerOutgoingTransferService.SendFile(package.InternalFile,
                package.InstructionSet.TransitOptions,
                TransferFileType.Normal,
                fileSystemType, odinContext,
                cn);
        }

        return recipientStatus;
    }

    private async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadataForNewFileOrOverwrite(
        FileUpdatePackage package,
        UploadFileDescriptor uploadDescriptor, IOdinContext odinContext)
    {
        var transferKeyEncryptedKeyHeader = uploadDescriptor!.EncryptedKeyHeader;

        if (null == transferKeyEncryptedKeyHeader)
        {
            throw new OdinClientException("Failure to unpack upload metadata, invalid transfer key header", OdinClientErrorCode.InvalidKeyHeader);
        }

        var clientSharedSecret = odinContext.PermissionsContext.SharedSecretKey;
        KeyHeader keyHeader = uploadDescriptor.FileMetadata.IsEncrypted
            ? transferKeyEncryptedKeyHeader.DecryptAesToKeyHeader(ref clientSharedSecret)
            : KeyHeader.Empty();

        await ValidateUploadDescriptor(uploadDescriptor);

        var metadata = await MapUploadToMetadata(package, uploadDescriptor, odinContext);

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = uploadDescriptor.FileMetadata.AccessControlList,
            AllowDistribution = uploadDescriptor.FileMetadata.AllowDistribution
        };

        return (keyHeader, metadata, serverMetadata);
    }

    /// <summary>
    /// Validates rules that apply to all files; regardless of being comment, standard, or some other type we've not yet conceived
    /// </summary>
    private async Task ValidateUploadCore(FileUpdatePackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata,
        DatabaseConnection cn)
    {
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
            if (ByteArrayUtil.IsStrongKey(keyHeader.Iv) == false || ByteArrayUtil.IsStrongKey(keyHeader.AesKey.GetKey()) == false)
            {
                throw new OdinClientException("Payload is set as encrypted but the encryption key is too simple",
                    code: OdinClientErrorCode.InvalidKeyHeader);
            }
        }
    }

    protected InternalDriveFileId MapToInternalFile(ExternalFileIdentifier file, IOdinContext odinContext)
    {
        return new InternalDriveFileId()
        {
            FileId = file.FileId,
            DriveId = odinContext.PermissionsContext.GetDriveId(file.TargetDrive)
        };
    }
}