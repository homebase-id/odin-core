using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Services.Drives.FileSystem.Base.Upload;

/// <summary>
/// Enables the writing of file streams from external sources and
/// rule enforcement specific to the type of file system
/// </summary>
public abstract class FileSystemStreamWriterBase
{
    private readonly TenantContext _tenantContext;


    private readonly DriveManager _driveManager;
    private readonly PeerOutgoingTransferService _peerOutgoingTransferService;
    private readonly ILogger _logger;

    /// <summary />
    protected FileSystemStreamWriterBase(
        IDriveFileSystem fileSystem,
        TenantContext tenantContext,
        DriveManager driveManager,
        PeerOutgoingTransferService peerOutgoingTransferService,
        ILogger logger)
    {
        FileSystem = fileSystem;

        _tenantContext = tenantContext;

        _driveManager = driveManager;
        _peerOutgoingTransferService = peerOutgoingTransferService;
        _logger = logger;
    }

    protected IDriveFileSystem FileSystem { get; }

    public FileUploadPackage Package { get; private set; }

    public virtual async Task StartUpload(Stream data, IOdinContext odinContext)
    {
        //TODO: need to partially encrypt upload instruction set
        string json = await new StreamReader(data).ReadToEndAsync();
        var instructionSet = OdinSystemSerializer.Deserialize<UploadInstructionSet>(json);

        await this.StartUpload(instructionSet, odinContext);
    }

    public virtual async Task StartUpload(UploadInstructionSet instructionSet, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(instructionSet, nameof(instructionSet));
        instructionSet?.AssertIsValid();

        if (instructionSet!.TransitOptions?.Recipients?.Contains(_tenantContext.HostOdinId) ?? false)
        {
            throw new OdinClientException("Cannot transfer to yourself; what's the point?", OdinClientErrorCode.InvalidRecipient);
        }

        InternalDriveFileId file;
        var driveId = odinContext.PermissionsContext.GetDriveId(instructionSet!.StorageOptions!.Drive);
        var overwriteFileId = instructionSet.StorageOptions?.OverwriteFileId.GetValueOrDefault() ?? Guid.Empty;

        // odinContext.PermissionsContext.AssertCanWriteToDrive(driveId);

        bool isUpdateOperation = false;

        if (overwriteFileId == Guid.Empty)
        {
            //get a new file id
            file = await FileSystem.Storage.CreateInternalFileId(driveId);
        }
        else
        {
            isUpdateOperation = true;
            //file to overwrite
            file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = overwriteFileId
            };
        }

        instructionSet.Manifest?.ResetPayloadUiDs();

        this.Package = new FileUploadPackage(file, instructionSet!, isUpdateOperation);
    }

    public virtual async Task AddMetadata(Stream data, IOdinContext odinContext)
    {
        // await FileSystem.Storage.WriteTempStream(Package.InternalFile, MultipartUploadParts.Metadata.ToString(), data);
        await FileSystem.Storage.WriteTempStream(Package.TempMetadataFile, MultipartUploadParts.Metadata.ToString(), data, odinContext);
    }

    public virtual async Task AddPayload(string key, string contentTypeFromMultipartSection, Stream data, IOdinContext odinContext)
    {
        if (Package.Payloads.Any(p => string.Equals(key, p.PayloadKey, StringComparison.InvariantCultureIgnoreCase)))
        {
            throw new OdinClientException($"Duplicate Payload key with key {key} has already been added",
                OdinClientErrorCode.InvalidUpload);
        }

        var descriptor = Package.InstructionSet.Manifest?.PayloadDescriptors.SingleOrDefault(pd => pd.PayloadKey == key);

        if (null == descriptor)
        {
            throw new OdinClientException($"Cannot find descriptor for payload key {key}", OdinClientErrorCode.InvalidUpload);
        }

        var extension = DriveFileUtility.GetPayloadFileExtension(key, descriptor.PayloadUid);
        var bytesWritten = await FileSystem.Storage.WriteTempStream(Package.InternalFile, extension, data, odinContext);

        if (bytesWritten > 0)
        {
            Package.Payloads.Add(descriptor.PackagePayloadDescriptor(bytesWritten, contentTypeFromMultipartSection));
        }
    }

    public virtual async Task AddThumbnail(string thumbnailUploadKey, string overrideContentType, Stream data, IOdinContext odinContext)
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

        var bytesWritten = await FileSystem.Storage.WriteTempStream(Package.InternalFile, extenstion, data, odinContext);

        Package.Thumbnails.Add(new PackageThumbnailDescriptor()
        {
            PixelHeight = result.ThumbnailDescriptor.PixelHeight,
            PixelWidth = result.ThumbnailDescriptor.PixelWidth,
            ContentType = string.IsNullOrEmpty(result.ThumbnailDescriptor.ContentType?.Trim())
                ? overrideContentType
                : result.ThumbnailDescriptor.ContentType,
            PayloadKey = result.PayloadKey,
            BytesWritten = bytesWritten
        });
    }

    /// <summary>
    /// Processes the instruction set on the specified packaged.  Used when all parts have been uploaded.
    /// </summary>
    public async Task<UploadResult> FinalizeUploadAsync(IOdinContext odinContext)
    {
        _logger.LogDebug("Entering FinalizeUploadAsync");

        var (keyHeader, metadata, serverMetadata) = await UnpackMetadata(Package, odinContext);

        await this.ValidateUploadCoreAsync(Package, keyHeader, metadata, serverMetadata);

        await this.ValidateUnpackedData(Package, keyHeader, metadata, serverMetadata, odinContext);

        if (Package.IsUpdateOperation)
        {
            // Validate the file exists by the File Id
            if (!await FileSystem.Storage.FileExists(Package.InternalFile, odinContext))
            {
                throw new OdinClientException("OverwriteFileId is specified but file does not exist",
                    OdinClientErrorCode.CannotOverwriteNonExistentFile);
            }

            if (metadata.VersionTag == null)
            {
                throw new OdinClientException("Missing version tag for update operation", OdinClientErrorCode.MissingVersionTag);
            }

            // If the uniqueId is being changed, validate that uniqueId is not in use by another file
            if (metadata.AppData.UniqueId.HasValue)
            {
                var incomingClientUniqueId = metadata.AppData.UniqueId.Value;
                var existingFileHeader = await FileSystem.Storage.GetServerFileHeader(Package.InternalFile, odinContext);

                var isChangingUniqueId = incomingClientUniqueId != existingFileHeader.FileMetadata.AppData.UniqueId;
                if (isChangingUniqueId)
                {
                    var existingFile =
                        await FileSystem.Query.GetFileByClientUniqueId(Package.InternalFile.DriveId, incomingClientUniqueId, odinContext);
                    if (null != existingFile && existingFile.FileId != existingFileHeader.FileMetadata.File.FileId)
                    {
                        throw new OdinClientException(
                            $"It looks like the uniqueId is being changed but a file already exists with ClientUniqueId: [{incomingClientUniqueId}]",
                            OdinClientErrorCode.ExistingFileWithUniqueId);
                    }
                }
            }

            await ProcessExistingFileUpload(Package, keyHeader, metadata, serverMetadata, odinContext);
        }
        else
        {
            // New file with UniqueId must not exist
            if (metadata.AppData.UniqueId.HasValue)
            {
                var incomingClientUniqueId = metadata.AppData.UniqueId.Value;
                var existingFile =
                    await FileSystem.Query.GetFileByClientUniqueId(Package.InternalFile.DriveId, incomingClientUniqueId, odinContext);
                if (null != existingFile && existingFile.FileState != FileState.Deleted)
                {
                    throw new OdinClientException($"File already exists with ClientUniqueId: [{incomingClientUniqueId}]",
                        OdinClientErrorCode.ExistingFileWithUniqueId);
                }
            }

            await ProcessNewFileUpload(Package, keyHeader, metadata, serverMetadata, odinContext);
        }

        Dictionary<string, TransferStatus> recipientStatus = await ProcessTransitInstructions(Package, odinContext);

        var uploadResult = new UploadResult()
        {
            NewVersionTag = metadata.VersionTag.GetValueOrDefault(),
            File = new ExternalFileIdentifier()
            {
                TargetDrive = (await _driveManager.GetDriveAsync(Package.InternalFile.DriveId)).TargetDriveInfo,
                FileId = Package.InternalFile.FileId
            },
            GlobalTransitId = metadata.GlobalTransitId,
            RecipientStatus = recipientStatus
        };

        _logger.LogDebug("Leaving FinalizeUploadAsync");

        return uploadResult;
    }

    /// <summary>
    /// Validates the uploaded data is correct before mapping to its final form.
    /// </summary>
    /// <param name="uploadDescriptor"></param>
    /// <returns></returns>
    protected abstract Task ValidateUploadDescriptor(UploadFileDescriptor uploadDescriptor);

    protected abstract Task ValidateUnpackedData(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata,
        IOdinContext odinContext);

    /// <summary>
    /// Called when the incoming file does not exist on disk.  This is called after core validations are complete
    /// </summary>
    protected abstract Task ProcessNewFileUpload(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata,
        IOdinContext odinContext);

    /// <summary>
    /// Called when then uploaded file exists on disk.  This is called after core validations are complete
    /// </summary>
    protected abstract Task ProcessExistingFileUpload(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata,
        IOdinContext odinContext);

    /// <summary>
    /// Called after the file is uploaded to process how transit will deal w/ the instructions
    /// </summary>
    /// <returns></returns>
    protected abstract Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(FileUploadPackage package,
        IOdinContext odinContext);

    /// <summary>
    /// Maps the uploaded file to the <see cref="FileMetadata"/> which will be stored on disk,
    /// </summary>
    /// <returns></returns>
    protected abstract Task<FileMetadata> MapUploadToMetadata(FileUploadPackage package, UploadFileDescriptor uploadDescriptor,
        IOdinContext odinContext);

    protected virtual async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadata(
        FileUploadPackage package,
        IOdinContext odinContext)
    {
        var clientSharedSecret = odinContext.PermissionsContext.SharedSecretKey;

        var metadataBytes =
            await FileSystem.Storage.GetAllFileBytesFromTempFile(package.TempMetadataFile, MultipartUploadParts.Metadata.ToString(),
                odinContext);
        var decryptedJsonBytes = AesCbc.Decrypt(metadataBytes, clientSharedSecret, package.InstructionSet.TransferIv);
        var uploadDescriptor = OdinSystemSerializer.Deserialize<UploadFileDescriptor>(decryptedJsonBytes.ToStringFromUtf8Bytes());

        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.MetadataOnly)
        {
            var (iv, metadata, serverMetadata) = await UnpackForMetadataUpdate(package, uploadDescriptor, odinContext);
            return (keyHeader: new KeyHeader()
                {
                    Iv = iv,
                    AesKey = new SensitiveByteArray(Guid.Empty.ToByteArray())
                },
                metadata,
                serverMetadata);
        }

        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.NewFileOrOverwrite)
        {
            return await UnpackMetadataForNewFileOrOverwrite(package, uploadDescriptor, odinContext);
        }

        throw new OdinSystemException("Unhandled storage intent");
    }

    protected async Task<Dictionary<string, TransferStatus>> ProcessTransitBasic(FileUploadPackage package, FileSystemType fileSystemType,
        IOdinContext odinContext)
    {
        Dictionary<string, TransferStatus> recipientStatus = null;

        var transitOptions = package.InstructionSet.TransitOptions;
        if (null == transitOptions)
        {
            return new Dictionary<string, TransferStatus>();
        }

        if (transitOptions.Recipients?.Any() ?? false)
        {
            OdinValidationUtils.AssertValidRecipientList(transitOptions.Recipients, allowEmpty: true);

            recipientStatus = await _peerOutgoingTransferService.SendFile(package.InternalFile,
                package.InstructionSet.TransitOptions,
                TransferFileType.Normal,
                fileSystemType, odinContext);

            return recipientStatus;
        }

        // Added for community support to allow a collab identity to
        // send a peer notification when the owner uploads a file
        if (transitOptions.UseAppNotification && (transitOptions.AppNotificationOptions.Recipients?.Any() ?? false))
        {
            var drive = await _driveManager.GetDriveAsync(package.InternalFile.DriveId, true);
            if (!drive.IsCollaborationDrive() || !drive.AllowSubscriptions)
            {
                throw new OdinClientException("App notification recipients can only be specified if the drive is a " +
                                              "collaboration drive and allows subscriptions");
            }

            await _peerOutgoingTransferService.SendPeerPushNotification(
                transitOptions.AppNotificationOptions,
                package.InternalFile.DriveId,
                odinContext);
        }

        return recipientStatus;
    }

    private async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadataForNewFileOrOverwrite(
        FileUploadPackage package,
        UploadFileDescriptor uploadDescriptor, IOdinContext odinContext)
    {
        var transferKeyEncryptedKeyHeader = uploadDescriptor!.EncryptedKeyHeader;

        if (null == transferKeyEncryptedKeyHeader)
        {
            throw new OdinClientException("Failure to unpack upload metadata, invalid transfer key header",
                OdinClientErrorCode.InvalidKeyHeader);
        }

        var clientSharedSecret = odinContext.PermissionsContext.SharedSecretKey;
        KeyHeader keyHeader = uploadDescriptor.FileMetadata.IsEncrypted
            ? transferKeyEncryptedKeyHeader.DecryptAesToKeyHeader(ref clientSharedSecret)
            : KeyHeader.Empty();

        await ValidateUploadDescriptor(uploadDescriptor);

        var metadata = await MapUploadToMetadata(package, uploadDescriptor, odinContext);

        var serverMetadata = new ServerMetadata
        {
            AccessControlList = uploadDescriptor.FileMetadata.AccessControlList,
            AllowDistribution = uploadDescriptor.FileMetadata.AllowDistribution,
            OriginalRecipientCount = package.InstructionSet.TransitOptions?.Recipients?.Count ?? 0
        };

        return (keyHeader, metadata, serverMetadata);
    }

    private async Task<(byte[] iv, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackForMetadataUpdate(FileUploadPackage package,
        UploadFileDescriptor uploadDescriptor, IOdinContext odinContext)
    {
        byte[] iv = null;

        if (uploadDescriptor.FileMetadata.IsEncrypted)
        {
            var transferKeyEncryptedKeyHeader = uploadDescriptor!.EncryptedKeyHeader;

            if (null == transferKeyEncryptedKeyHeader)
            {
                throw new OdinClientException("Failure to unpack upload metadata, invalid transfer key header",
                    OdinClientErrorCode.InvalidKeyHeader);
            }

            var clientSharedSecret = odinContext.PermissionsContext.SharedSecretKey;
            KeyHeader keyHeader = transferKeyEncryptedKeyHeader.DecryptAesToKeyHeader(ref clientSharedSecret);

            if (!ByteArrayUtil.IsStrongKey(keyHeader.Iv))
            {
                throw new OdinClientException($"You must specify a new IV when storage intent is {StorageIntent.MetadataOnly}",
                    OdinClientErrorCode.MalformedMetadata);
            }

            if (!ByteArrayUtil.EquiByteArrayCompare(keyHeader.AesKey.GetKey(), Guid.Empty.ToByteArray()))
            {
                throw new OdinClientException(
                    $"You must specify a 16-byte all-zero Aes Key when file is encrypted and  storage intent is {StorageIntent.MetadataOnly}",
                    OdinClientErrorCode.MalformedMetadata);
            }

            iv = keyHeader.Iv;
        }

        await ValidateUploadDescriptor(uploadDescriptor);

        var metadata = await MapUploadToMetadata(package, uploadDescriptor, odinContext);

        if (metadata.Payloads?.Any() ?? false)
        {
            throw new OdinClientException($"Cannot specify additional payloads when storage intent is {StorageIntent.MetadataOnly}",
                OdinClientErrorCode.MalformedMetadata);
        }

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = uploadDescriptor.FileMetadata.AccessControlList,
            AllowDistribution = uploadDescriptor.FileMetadata.AllowDistribution
        };

        return (iv, metadata, serverMetadata);
    }

    /// <summary>
    /// Validates rules that apply to all files; regardless of being comment, standard, or some other type we've not yet conceived
    /// </summary>
    private async Task ValidateUploadCoreAsync(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata)
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
                throw new OdinClientException("All payload IVs must be 0 bytes when server file header is not encrypted",
                    OdinClientErrorCode.InvalidUpload);
            }

            if (metadata.IsEncrypted && !Package.Payloads.All(p => p.HasStrongIv()))
            {
                throw new OdinClientException("When the file is encrypted, you must specify a valid payload IV of 16 bytes",
                    OdinClientErrorCode.InvalidUpload);
            }
        }

        var drive = await _driveManager.GetDriveAsync(package.InternalFile.DriveId, true);
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

        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.NewFileOrOverwrite)
        {
            if (metadata.IsEncrypted)
            {
                if (ByteArrayUtil.IsStrongKey(keyHeader.Iv) == false || ByteArrayUtil.IsStrongKey(keyHeader.AesKey.GetKey()) == false)
                {
                    throw new OdinClientException("Payload is set as encrypted but the encryption key is too simple",
                        code: OdinClientErrorCode.InvalidKeyHeader);
                }
            }
        }
        
        DriveFileUtility.AssertValidAppContentLength(metadata.AppData?.Content ?? "");
        DriveFileUtility.AssertValidPreviewThumbnail(metadata.AppData?.PreviewThumbnail);
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