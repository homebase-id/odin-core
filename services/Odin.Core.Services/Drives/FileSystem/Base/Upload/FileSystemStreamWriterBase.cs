using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;

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

    /// <summary />
    protected FileSystemStreamWriterBase(IDriveFileSystem fileSystem, TenantContext tenantContext, OdinContextAccessor contextAccessor,
        DriveManager driveManager)
    {
        FileSystem = fileSystem;

        _tenantContext = tenantContext;
        _contextAccessor = contextAccessor;
        _driveManager = driveManager;
    }

    protected IDriveFileSystem FileSystem { get; }

    public UploadPackage Package { get; private set; }

    public virtual async Task StartUpload(Stream data)
    {
        //TODO: need to partially encrypt upload instruction set
        string json = await new StreamReader(data).ReadToEndAsync();
        var instructionSet = OdinSystemSerializer.Deserialize<UploadInstructionSet>(json);

        await this.StartUpload(instructionSet);
    }

    public virtual async Task StartUpload(UploadInstructionSet instructionSet)
    {
        Guard.Argument(instructionSet, nameof(instructionSet)).NotNull();
        instructionSet?.AssertIsValid();

        if (instructionSet!.TransitOptions?.Recipients?.Contains(_tenantContext.HostOdinId) ?? false)
        {
            throw new OdinClientException("Cannot transfer to yourself; what's the point?", OdinClientErrorCode.InvalidRecipient);
        }

        InternalDriveFileId file;
        var driveId = _driveManager.GetDriveIdByAlias(instructionSet!.StorageOptions!.Drive, true).Result.GetValueOrDefault();
        var overwriteFileId = instructionSet?.StorageOptions?.OverwriteFileId.GetValueOrDefault() ?? Guid.Empty;

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

        this.Package = new UploadPackage(file, instructionSet!, isUpdateOperation);
        await Task.CompletedTask;
    }

    public virtual async Task AddMetadata(Stream data)
    {
        await FileSystem.Storage.WriteTempStream(Package.InternalFile, MultipartUploadParts.Metadata.ToString(), data);
    }


    public virtual async Task AddPayload(Stream data)
    {
        var bytesWritten = await FileSystem.Storage.WriteTempStream(Package.InternalFile, MultipartUploadParts.Payload.ToString(), data);
        Package.HasPayload = bytesWritten > 0;
    }

    public virtual async Task AddThumbnail(int width, int height, string contentType, Stream data)
    {
        //TODO: How to store the content type for later usage?  is it even needed?

        //TODO: should i validate width and height are > 0?
        string extenstion = FileSystem.Storage.GetThumbnailFileExtension(width, height);
        await FileSystem.Storage.WriteTempStream(Package.InternalFile, extenstion, data);

        Package.UploadedThumbnails.Add(new ImageDataHeader()
        {
            PixelHeight = height,
            PixelWidth = width,
            ContentType = contentType
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
            // Validate the file exists by the Id
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

        // _uploadLock.ReleaseLock(metadata.File);

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

    protected abstract Task ValidateUnpackedData(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata);

    /// <summary>
    /// Called when the incoming file does not exist on disk.  This is called after core validations are complete
    /// </summary>
    protected abstract Task ProcessNewFileUpload(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata);

    /// <summary>
    /// Called when then uploaded file exists on disk.  This is called after core validations are complete
    /// </summary>
    protected abstract Task ProcessExistingFileUpload(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata);

    /// <summary>
    /// Called after the file is uploaded to process how transit will deal w/ the instructions
    /// </summary>
    /// <returns></returns>
    protected abstract Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(UploadPackage package);

    /// <summary>
    /// Maps the uploaded file to the <see cref="FileMetadata"/> which will be stored on disk,
    /// </summary>
    /// <returns></returns>
    protected abstract Task<FileMetadata> MapUploadToMetadata(UploadPackage package, UploadFileDescriptor uploadDescriptor);

    protected virtual async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadata(UploadPackage package)
    {
        var metadataStream = await FileSystem.Storage.GetTempStream(package.InternalFile, MultipartUploadParts.Metadata.ToString());

        var clientSharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
        var decryptedJsonBytes = AesCbc.Decrypt(metadataStream.ToByteArray(), ref clientSharedSecret, package.InstructionSet.TransferIv);
        metadataStream.Close();

        var json = System.Text.Encoding.UTF8.GetString(decryptedJsonBytes);

        var uploadDescriptor = OdinSystemSerializer.Deserialize<UploadFileDescriptor>(json);

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

    private async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadataForNewFileOrOverwrite(UploadPackage package,
        UploadFileDescriptor uploadDescriptor)
    {
        var transferKeyEncryptedKeyHeader = uploadDescriptor!.EncryptedKeyHeader;

        if (null == transferKeyEncryptedKeyHeader)
        {
            throw new OdinClientException("Failure to unpack upload metadata, invalid transfer key header", OdinClientErrorCode.InvalidKeyHeader);
        }

        var clientSharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
        KeyHeader keyHeader = uploadDescriptor.FileMetadata.PayloadIsEncrypted
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

    private async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackForMetadataUpdate(UploadPackage package,
        UploadFileDescriptor uploadDescriptor)
    {
        if (uploadDescriptor.EncryptedKeyHeader?.EncryptedAesKey?.Length > 0)
        {
            throw new OdinClientException($"Cannot specify key header when storage intent is {StorageIntent.MetadataOnly}",
                OdinClientErrorCode.MalformedMetadata);
        }

        await ValidateUploadDescriptor(uploadDescriptor);

        var metadata = await MapUploadToMetadata(package, uploadDescriptor);

        if (metadata.AppData.AdditionalThumbnails?.Any() ?? false)
        {
            throw new OdinClientException($"Cannot specify additional thumbnails when storage intent is {StorageIntent.MetadataOnly}",
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
    private async Task ValidateUploadCore(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        if (null == serverMetadata.AccessControlList)
        {
            throw new MissingDataException("Access control list must be specified");
        }

        serverMetadata.AccessControlList.Validate();

        if (serverMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Anonymous && metadata.PayloadIsEncrypted)
        {
            //Note: dont allow anonymously accessible encrypted files because we wont have a client shared secret to secure the key header
            throw new OdinClientException("Cannot upload an encrypted file that is accessible to anonymous visitors",
                OdinClientErrorCode.CannotUploadEncryptedFileForAnonymous);
        }

        if (serverMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Authenticated && metadata.PayloadIsEncrypted)
        {
            throw new OdinClientException("Cannot upload an encrypted file that is accessible to authenticated visitors",
                OdinClientErrorCode.CannotUploadEncryptedFileForAnonymous);
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
            if (metadata.AppData.ContentIsComplete && package.HasPayload)
            {
                throw new OdinClientException("Content is marked complete in metadata but there is also a payload", OdinClientErrorCode.InvalidPayload);
            }

            if (metadata.AppData.ContentIsComplete == false && package.HasPayload == false)
            {
                throw new OdinClientException("Content is marked incomplete yet there is no payload", OdinClientErrorCode.InvalidPayload);
            }

            if ((metadata.AppData.AdditionalThumbnails?.Count() ?? 0) != (package.UploadedThumbnails?.Count() ?? 0))
            {
                //TODO: technically we could just detect the thumbnails instead of making the user specify AdditionalThumbnails
                throw new OdinClientException("The number of additional thumbnails in your appData section does not match the number of thumbnails uploaded.",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }

            if (metadata.PayloadIsEncrypted)
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