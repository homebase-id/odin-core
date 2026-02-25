using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bitcoin.BitcoinUtilities;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
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
    private readonly IDriveManager _driveManager;
    private readonly PeerOutgoingTransferService _peerOutgoingTransferService;
    private readonly ILogger _logger;

    /// <summary />
    protected FileSystemUpdateWriterBase(IDriveFileSystem fileSystem, IDriveManager driveManager,
        PeerOutgoingTransferService peerOutgoingTransferService,
        ILogger logger)
    {
        FileSystem = fileSystem;
        _driveManager = driveManager;
        _peerOutgoingTransferService = peerOutgoingTransferService;
        _logger = logger;
    }

    protected IDriveFileSystem FileSystem { get; }

    internal FileUpdatePackage Package { get; private set; }

    public virtual async Task StartFileUpdateAsync(FileUpdateInstructionSet instructionSet, FileSystemType fileSystemType,
        IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(instructionSet, nameof(instructionSet));
        instructionSet.AssertIsValid();
        OdinValidationUtils.AssertValidRecipientList(instructionSet.Recipients, allowEmpty: true, odinContext.Tenant);

        instructionSet.Manifest.ResetPayloadUiDs();

        if (instructionSet.Locale == UpdateLocale.Local)
        {
            // instructionSet.File.AssertIsValid(expectedType: FileIdentifierType.File);
            instructionSet.File.AssertIsValid();

            //  There must be a local file that will be updated - then sent out to recipients
            //  the outbox is used, and we can write the result to the local file in the transfer history

            // resolve the file by one of the specified Ids

            var fileId = await ResolveLocalFileId(instructionSet.File, odinContext);

            if (null == fileId)
            {
                throw new OdinClientException("Cannot resolve file", OdinClientErrorCode.InvalidFile);
            }
            
            // File to overwrite
            InternalDriveFileId file = new InternalDriveFileId()
            {
                DriveId = instructionSet.File.TargetDrive.Alias,
                FileId = fileId.GetValueOrDefault()
            };

            this.Package = new FileUpdatePackage(file)
            {
                InstructionSet = instructionSet,
                FileSystemType = fileSystemType
            };

            await Task.CompletedTask;
            return;
        }

        if (instructionSet.Locale == UpdateLocale.Peer)
        {
            //Note: there is no local file.  everything is enqueued to the transient temp drive
            OdinValidationUtils.AssertValidRecipientList(instructionSet.Recipients, false, odinContext.Tenant);
            instructionSet.File.AssertIsValid(FileIdentifierType.GlobalTransitId);

            InternalDriveFileId file = new InternalDriveFileId()
            {
                DriveId = SystemDriveConstants.TransientTempDrive.Alias,
                FileId = Guid.NewGuid() // Note: in the case of peer, there is no local file so we just put a random value in here that will never be used
            };

            this.Package = new FileUpdatePackage(file)
            {
                InstructionSet = instructionSet,
                FileSystemType = fileSystemType
            };
            return;
        }

        throw new NotImplementedException("Unhandled locale specified");
    }

    private async Task<Guid?> ResolveLocalFileId(FileIdentifier fileIdentifier, IOdinContext odinContext)
    {
        var type = fileIdentifier.GetFileIdentifierType();
        switch (type)
        {
            case FileIdentifierType.File:
                return fileIdentifier.FileId;

            case FileIdentifierType.GlobalTransitId:
                var fileByGlobalTransitId = await FileSystem.Query.GetFileByGlobalTransitId(fileIdentifier.TargetDrive.Alias,
                    fileIdentifier.GlobalTransitId.GetValueOrDefault(), odinContext);
                return fileByGlobalTransitId?.FileId;

            case FileIdentifierType.UniqueId:
                var fileByClientUniqueId = await FileSystem.Query.GetFileByClientUniqueIdForWriting(fileIdentifier.TargetDrive.Alias,
                    fileIdentifier.UniqueId.GetValueOrDefault(), odinContext);
                return fileByClientUniqueId?.FileId;
            default:
                throw new OdinClientException("Unknown file identifier");
        }
    }

    public virtual Task AddMetadata(Stream data)
    {
        Package.Metadata = data.ToByteArray();
        return Task.CompletedTask;
    }

    public virtual async Task AddPayload(string key, string contentTypeFromMultipartSection, Stream data, IOdinContext odinContext)
    {
        if (Package.Payloads.Any(p => p.KeyEquals(key)))
        {
            throw new OdinClientException($"Duplicate Payload key with key {key} has already been added",
                OdinClientErrorCode.InvalidUpload);
        }

        var descriptor = Package.InstructionSet.Manifest?.PayloadDescriptors.SingleOrDefault(pd => pd.PayloadKey == key);

        if (null == descriptor)
        {
            throw new OdinClientException($"Cannot find descriptor for payload key {key}", OdinClientErrorCode.InvalidUpload);
        }

        var extension = TenantPathManager.GetBasePayloadFileNameAndExtension(key, descriptor.PayloadUid);

        var bytesWritten = await FileSystem.Storage.WriteTempStream(new UploadFile(Package.InternalFile), extension, data, odinContext);

        if (bytesWritten != data.Length)
        {
            throw new OdinSystemException(
                $"Failed to write all expected data in stream. Wrote {bytesWritten} but should have been {data.Length}");
        }

        Package.Payloads.Add(descriptor.PackagePayloadDescriptor(bytesWritten, contentTypeFromMultipartSection));
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
        string extension = TenantPathManager.GetThumbnailFileNameAndExtension(
            result.PayloadKey,
            result.PayloadUid,
            result.ThumbnailDescriptor.PixelWidth,
            result.ThumbnailDescriptor.PixelHeight
        );

        var bytesWritten = await FileSystem.Storage.WriteTempStream(new UploadFile(Package.InternalFile), extension, data, odinContext);

        if (bytesWritten != data.Length)
        {
            throw new OdinSystemException(
                $"Failed to write all expected data in stream. Wrote {bytesWritten} but should have been {data.Length}");
        }

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

    public async Task<FileUpdateResult> FinalizeFileUpdate(IOdinContext odinContext)
    {
        var (keyHeader, metadata, serverMetadata) = await UnpackMetadata(Package, odinContext);

        await this.ValidateUploadCore(Package, keyHeader, metadata, serverMetadata, odinContext);

        if (Package.InstructionSet.Locale == UpdateLocale.Local)
        {
            if (metadata.VersionTag == null)
            {
                throw new OdinClientException("Missing version tag for update operation", OdinClientErrorCode.MissingVersionTag);
            }

            await ProcessExistingFileUploadAsync(Package, keyHeader, metadata, serverMetadata, odinContext);

            var existingHeader = await FileSystem.Storage.GetServerFileHeader(Package.InternalFile, odinContext);

            var recipientStatus = await ProcessTransitInstructions(Package,
                new FileIdentifier()
                {
                    GlobalTransitId = existingHeader.FileMetadata.GlobalTransitId,
                    TargetDrive = Package.InstructionSet.File.TargetDrive
                },
                keyHeader,
                odinContext);

            return new FileUpdateResult
            {
                File = new ExternalFileIdentifier()
                {
                    FileId = metadata.File.FileId,
                    TargetDrive = Package.InstructionSet.File.TargetDrive
                },
                GlobalTransitId = metadata.GlobalTransitId,
                NewVersionTag = metadata.VersionTag.GetValueOrDefault(),
                RecipientStatus = recipientStatus,
            };
        }

        if (Package.InstructionSet.Locale == UpdateLocale.Peer)
        {
            // Note: all changes on remote servers need to use the Package.NewVersionTag
            // There is no local file - everything would be on the temp-transient-drive

            // We send a version tag to other identities so that we can also return it to
            // the local caller since currently, there is no method to get back info from
            // the outbox when sending transient files.

            if (!serverMetadata.AllowDistribution)
            {
                throw new OdinClientException("AllowDistribution must be true when UpdateLocale is Peer");
            }

            await FileSystem.Storage.CommitNewFile(new UploadFile(Package.InternalFile), keyHeader, metadata, serverMetadata, false,
                odinContext);

            var recipientStatus = await ProcessTransitInstructions(Package, Package.InstructionSet.File, keyHeader, odinContext);

            return new FileUpdateResult()
            {
                File = new ExternalFileIdentifier()
                {
                    FileId = Guid.Empty,
                    TargetDrive = new TargetDrive()
                    {
                        Alias = GuidId.Empty,
                        Type = GuidId.Empty
                    }
                },
                GlobalTransitId = metadata.GlobalTransitId,
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
    protected virtual async Task ProcessExistingFileUploadAsync(FileUpdatePackage package, KeyHeader keyHeader,
        FileMetadata metadata,
        ServerMetadata serverMetadata,
        IOdinContext odinContext)
    {
        var manifest = new BatchUpdateManifest()
        {
            NewVersionTag = Package.NewVersionTag,
            PayloadInstruction = package.InstructionSet.Manifest.PayloadDescriptors?.Select(p => new PayloadInstruction()
            {
                Key = p.PayloadKey,
                OperationType = p.PayloadUpdateOperationType
            }).ToList(),

            KeyHeader = keyHeader,
            FileMetadata = metadata,
            ServerMetadata = serverMetadata
        };

        // TODO what if success is false?
        var (success, payloads) = await FileSystem.Storage.UpdateBatchAsync(new UploadFile(package.InternalFile), package.InternalFile,
            manifest, odinContext, null);

        if (success == false)
            throw new OdinClientException("No, I couldn't do it, success is false");
    }

    /// <summary>
    /// Maps the uploaded file to the <see cref="FileMetadata"/> which will be stored on disk,
    /// </summary>
    /// <returns></returns>
    protected abstract Task<FileMetadata> MapUploadToMetadata(FileUpdatePackage package, UpdateFileDescriptor updateDescriptor,
        IOdinContext odinContext);

    protected virtual async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadata(
        FileUpdatePackage package,
        IOdinContext odinContext)
    {
        var clientSharedSecret = odinContext.PermissionsContext.SharedSecretKey;

        var decryptedJsonBytes = AesCbc.Decrypt(package.Metadata, clientSharedSecret, package.InstructionSet.TransferIv);
        var updateDescriptor = OdinSystemSerializer.Deserialize<UpdateFileDescriptor>(decryptedJsonBytes.ToStringFromUtf8Bytes());

        KeyHeader keyHeader = updateDescriptor.FileMetadata.IsEncrypted
            ? updateDescriptor.EncryptedKeyHeader.DecryptAesToKeyHeader(ref clientSharedSecret)
            : KeyHeader.Empty();

        await ValidateUploadDescriptor(updateDescriptor);

        var metadata = await MapUploadToMetadata(package, updateDescriptor, odinContext);
        metadata.Validate(odinContext.Tenant);

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = updateDescriptor.FileMetadata.AccessControlList,
            AllowDistribution = updateDescriptor.FileMetadata.AllowDistribution
        };

        return (keyHeader, metadata, serverMetadata);
    }

    protected virtual async Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(FileUpdatePackage package,
        FileIdentifier targetFile,
        KeyHeader keyHeader,
        IOdinContext odinContext)
    {
        Dictionary<string, TransferStatus> recipientStatus = null;
        var recipients = package.InstructionSet.Recipients;

        if (recipients?.Any() ?? false)
        {
            OdinValidationUtils.AssertValidRecipientList(recipients);
            targetFile.AssertIsValid(FileIdentifierType.GlobalTransitId);

            recipientStatus = await _peerOutgoingTransferService.UpdateFile(
                package.InternalFile,
                keyHeader,
                targetFile,
                package.InstructionSet.Manifest,
                package.InstructionSet.Recipients,
                package.NewVersionTag,
                package.FileSystemType,
                package.InstructionSet.UseAppNotification ? package.InstructionSet.AppNotificationOptions : null,
                package.InstructionSet.Locale,
                odinContext,
                overrideDataSource: null);
        }

        return recipientStatus;
    }

    /// <summary>
    /// Validates rules that apply to all files; regardless of being comment, standard, or some other type we've not yet conceived
    /// </summary>
    private async Task ValidateUploadCore(FileUpdatePackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata, IOdinContext odinContext)
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

        if (metadata.IsEncrypted)
        {
            if (ByteArrayUtil.IsStrongKey(keyHeader.Iv) == false ||
                ByteArrayUtil.IsStrongKey(keyHeader.AesKey.GetKey()) == false)
            {
                throw new OdinClientException(
                    $"The encryption key is too simple: IV [{Utilities.BytesToHexString(keyHeader.Iv)}], the AesKey is {keyHeader.AesKey.GetKey().Length} long.",
                    code: OdinClientErrorCode.InvalidKeyHeader);
            }
        }

        metadata.Validate(odinContext.Tenant);
    }

    public async Task CleanupTempFiles(IOdinContext odinContext)
    {
        if (Package?.Payloads?.Any() ?? false)
        {
            await FileSystem.Storage.CleanupUploadTemporaryFiles(
                new UploadFile(Package.InternalFile),
                Package.GetFinalPayloadDescriptors(),
                odinContext);
        }
    }
}