using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Core.Services.Util;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload;

/// <summary>
/// Enables the writing of file streams from external sources and
/// rule enforcement specific to the type of file system
/// </summary>
public abstract class FileSystemStreamWriterBase
{
    private readonly TenantContext _tenantContext;
    private readonly OdinContextAccessor _contextAccessor;

    private readonly DriveManager _driveManager;
    private readonly IPeerOutgoingTransferService _peerOutgoingTransferService;

    /// <summary />
    protected FileSystemStreamWriterBase(IDriveFileSystem fileSystem, TenantContext tenantContext, OdinContextAccessor contextAccessor,
        DriveManager driveManager, IPeerOutgoingTransferService peerOutgoingTransferService)
    {
        FileSystem = fileSystem;

        _tenantContext = tenantContext;
        _contextAccessor = contextAccessor;
        _driveManager = driveManager;
        _peerOutgoingTransferService = peerOutgoingTransferService;
    }

    protected IDriveFileSystem FileSystem { get; }

    public FileUploadPackage Package { get; private set; }

    public virtual async Task StartUpload(Stream data)
    {
        //TODO: need to partially encrypt upload instruction set
        string json = await new StreamReader(data).ReadToEndAsync();
        var instructionSet = OdinSystemSerializer.Deserialize<UploadInstructionSet>(json);

        await this.StartUpload(instructionSet);
    }

    public virtual async Task StartUpload(UploadInstructionSet instructionSet)
    {
        OdinValidationUtils.AssertNotNull(instructionSet, nameof(instructionSet));
        instructionSet?.AssertIsValid();
        
        if (instructionSet!.TransitOptions?.Recipients?.Contains(_tenantContext.HostOdinId) ?? false)
        {
            throw new OdinClientException("Cannot transfer to yourself; what's the point?", OdinClientErrorCode.InvalidRecipient);
        }

        InternalDriveFileId file;
        var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(instructionSet!.StorageOptions!.Drive);
        var overwriteFileId = instructionSet.StorageOptions?.OverwriteFileId.GetValueOrDefault() ?? Guid.Empty;

        // _contextAccessor.GetCurrent().PermissionsContext.AssertCanWriteToDrive(driveId);

        bool isUpdateOperation = false;

        if (overwriteFileId == Guid.Empty)
        {
            //get a new file id
            file = FileSystem.Storage.CreateInternalFileId(driveId);
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

        if (instructionSet.Manifest?.PayloadDescriptors != null)
        {
            foreach (var pd in instructionSet.Manifest!.PayloadDescriptors)
            {
                //These are created in advance to ensure we can
                //upload thumbnails and payloads in any order
                pd.PayloadUid = UnixTimeUtcUnique.Now();
            }
        }

        this.Package = new FileUploadPackage(file, instructionSet!, isUpdateOperation);
        await Task.CompletedTask;
    }

    public virtual async Task AddMetadata(Stream data)
    {
        await FileSystem.Storage.WriteTempStream(Package.InternalFile, MultipartUploadParts.Metadata.ToString(), data);
    }

    public virtual async Task AddPayload(string key, string contentType, Stream data)
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
        var bytesWritten = await FileSystem.Storage.WriteTempStream(Package.InternalFile, extension, data);

        if (bytesWritten > 0)
        {
            Package.Payloads.Add(new PackagePayloadDescriptor()
            {
                Iv = descriptor.Iv,
                Uid = descriptor.PayloadUid,
                PayloadKey = key,
                ContentType = contentType,
                LastModified = UnixTimeUtc.Now(),
                BytesWritten = bytesWritten,
                DescriptorContent = descriptor.DescriptorContent,
                PreviewThumbnail = descriptor.PreviewThumbnail
            });
        }
    }

    public virtual async Task AddThumbnail(string thumbnailUploadKey, string contentType, Stream data)
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

        await FileSystem.Storage.WriteTempStream(Package.InternalFile, extenstion, data);

        Package.Thumbnails.Add(new PackageThumbnailDescriptor()
        {
            PixelHeight = result.ThumbnailDescriptor.PixelHeight,
            PixelWidth = result.ThumbnailDescriptor.PixelWidth,
            ContentType = contentType,
            PayloadKey = result.PayloadKey
        });
    }

    /// <summary>
    /// Processes the instruction set on the specified packaged.  Used when all parts have been uploaded.
    /// </summary>
    public async Task<UploadResult> FinalizeUpload()
    {
        var (keyHeader, metadata, serverMetadata) = await UnpackMetadata(Package);

        await this.ValidateUploadCore(Package, keyHeader, metadata, serverMetadata);

        await this.ValidateUnpackedData(Package, keyHeader, metadata, serverMetadata);

        if (Package.IsUpdateOperation)
        {
            // Validate the file exists by the File Id
            if (!FileSystem.Storage.FileExists(Package.InternalFile))
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
                var existingFileHeader = await FileSystem.Storage.GetServerFileHeader(Package.InternalFile);

                var isChangingUniqueId = incomingClientUniqueId != existingFileHeader.FileMetadata.AppData.UniqueId;
                if (isChangingUniqueId)
                {
                    var existingFile = await FileSystem.Query.GetFileByClientUniqueId(Package.InternalFile.DriveId, incomingClientUniqueId);
                    if (null != existingFile && existingFile.FileId != existingFileHeader.FileMetadata.File.FileId)
                    {
                        throw new OdinClientException(
                            $"It looks like the uniqueId is being changed but a file already exists with ClientUniqueId: [{incomingClientUniqueId}]",
                            OdinClientErrorCode.ExistingFileWithUniqueId);
                    }
                }
            }

            await ProcessExistingFileUpload(Package, keyHeader, metadata, serverMetadata);
        }
        else
        {
            // New file with UniqueId must not exist
            if (metadata.AppData.UniqueId.HasValue)
            {
                var incomingClientUniqueId = metadata.AppData.UniqueId.Value;
                var existingFile = await FileSystem.Query.GetFileByClientUniqueId(Package.InternalFile.DriveId, incomingClientUniqueId);
                if (null != existingFile && existingFile.FileState != FileState.Deleted)
                {
                    throw new OdinClientException($"File already exists with ClientUniqueId: [{incomingClientUniqueId}]",
                        OdinClientErrorCode.ExistingFileWithUniqueId);
                }
            }

            await ProcessNewFileUpload(Package, keyHeader, metadata, serverMetadata);
        }

        Dictionary<string, TransferStatus> recipientStatus = await ProcessTransitInstructions(Package);

        var uploadResult = new UploadResult()
        {
            NewVersionTag = metadata.VersionTag.GetValueOrDefault(),
            File = new ExternalFileIdentifier()
            {
                TargetDrive = _driveManager.GetDrive(Package.InternalFile.DriveId).Result.TargetDriveInfo,
                FileId = Package.InternalFile.FileId
            },
            GlobalTransitId = metadata.GlobalTransitId,
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

    protected abstract Task ValidateUnpackedData(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata);

    /// <summary>
    /// Called when the incoming file does not exist on disk.  This is called after core validations are complete
    /// </summary>
    protected abstract Task ProcessNewFileUpload(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata);

    /// <summary>
    /// Called when then uploaded file exists on disk.  This is called after core validations are complete
    /// </summary>
    protected abstract Task ProcessExistingFileUpload(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata);

    /// <summary>
    /// Called after the file is uploaded to process how transit will deal w/ the instructions
    /// </summary>
    /// <returns></returns>
    protected abstract Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(FileUploadPackage package);

    /// <summary>
    /// Maps the uploaded file to the <see cref="FileMetadata"/> which will be stored on disk,
    /// </summary>
    /// <returns></returns>
    protected abstract Task<FileMetadata> MapUploadToMetadata(FileUploadPackage package, UploadFileDescriptor uploadDescriptor);

    protected virtual async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadata(FileUploadPackage package)
    {
        var clientSharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;

        var metadataBytes = await FileSystem.Storage.GetAllFileBytes(package.InternalFile, MultipartUploadParts.Metadata.ToString());
        var decryptedJsonBytes = AesCbc.Decrypt(metadataBytes, clientSharedSecret, package.InstructionSet.TransferIv);
        var uploadDescriptor = OdinSystemSerializer.Deserialize<UploadFileDescriptor>(decryptedJsonBytes.ToStringFromUtf8Bytes());

        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.MetadataOnly)
        {
            return await UnpackForMetadataUpdate(package, uploadDescriptor);
        }

        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.NewFileOrOverwrite)
        {
            return await UnpackMetadataForNewFileOrOverwrite(package, uploadDescriptor);
        }

        throw new OdinSystemException("Unhandled storage intent");
    }

    protected async Task<Dictionary<string, TransferStatus>> ProcessTransitBasic(FileUploadPackage package, FileSystemType fileSystemType)
    {
        Dictionary<string, TransferStatus> recipientStatus = null;
        var recipients = package.InstructionSet.TransitOptions?.Recipients;

        OdinValidationUtils.AssertValidRecipientList(recipients, allowEmpty: true);

        if (recipients?.Any() ?? false)
        {
            recipientStatus = await _peerOutgoingTransferService.SendFile(package.InternalFile,
                package.InstructionSet.TransitOptions,
                TransferFileType.Normal,
                fileSystemType);
        }

        return recipientStatus;
    }

    private async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadataForNewFileOrOverwrite(
        FileUploadPackage package,
        UploadFileDescriptor uploadDescriptor)
    {
        var transferKeyEncryptedKeyHeader = uploadDescriptor!.EncryptedKeyHeader;

        if (null == transferKeyEncryptedKeyHeader)
        {
            throw new OdinClientException("Failure to unpack upload metadata, invalid transfer key header", OdinClientErrorCode.InvalidKeyHeader);
        }

        var clientSharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
        KeyHeader keyHeader = uploadDescriptor.FileMetadata.IsEncrypted
            ? transferKeyEncryptedKeyHeader.DecryptAesToKeyHeader(ref clientSharedSecret)
            : KeyHeader.Empty();

        await ValidateUploadDescriptor(uploadDescriptor);

        var metadata = await MapUploadToMetadata(package, uploadDescriptor);

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = uploadDescriptor.FileMetadata.AccessControlList,
            AllowDistribution = uploadDescriptor.FileMetadata.AllowDistribution
        };

        return (keyHeader, metadata, serverMetadata);
    }

    private async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackForMetadataUpdate(FileUploadPackage package,
        UploadFileDescriptor uploadDescriptor)
    {
        if (uploadDescriptor.EncryptedKeyHeader?.EncryptedAesKey?.Length > 0)
        {
            throw new OdinClientException($"Cannot specify key header when storage intent is {StorageIntent.MetadataOnly}",
                OdinClientErrorCode.MalformedMetadata);
        }

        await ValidateUploadDescriptor(uploadDescriptor);

        var metadata = await MapUploadToMetadata(package, uploadDescriptor);

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

        return (null, metadata, serverMetadata);
    }

    /// <summary>
    /// Validates rules that apply to all files; regardless of being comment, standard, or some other type we've not yet conceived
    /// </summary>
    private async Task ValidateUploadCore(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        if (null == serverMetadata.AccessControlList)
        {
            throw new MissingDataException("Access control list must be specified");
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

        var drive = await _driveManager.GetDrive(package.InternalFile.DriveId, true);
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


        //if a new file, we need to ensure the global transit is set correct.  for existing files, the system
        // uses the existing global transit id
        if (!package.IsUpdateOperation)
        {
            bool usesGlobalTransitId = package.InstructionSet.TransitOptions?.UseGlobalTransitId ?? false;
            if (serverMetadata.AllowDistribution && usesGlobalTransitId == false)
            {
                throw new OdinClientException(
                    "UseGlobalTransitId must be true when AllowDistribution is true. (Yes, yes I know, i could just do it for you but then you would be all - htf is this GlobalTransitId getting set.. ooommmggg?!  Then you would hunt through the code and we would end up with long debate in the issue list.  #aintnobodygottimeforthat <3.  just love me and set the param",
                    OdinClientErrorCode.InvalidTransitOptions);
            }
        }
    }

    protected InternalDriveFileId MapToInternalFile(ExternalFileIdentifier file)
    {
        return new InternalDriveFileId()
        {
            FileId = file.FileId,
            DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(file.TargetDrive)
        };
    }
}