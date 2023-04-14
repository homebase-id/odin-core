using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drives.FileSystem.Base.Upload;

//TODO: remove old packageId from methods

/// <summary>
/// Enables the writing of file streams from external sources and
/// rule enforcement specific to the type of file system
/// </summary>
public abstract class FileSystemStreamWriterBase
{
    private readonly TenantContext _tenantContext;
    private readonly DotYouContextAccessor _contextAccessor;

    private UploadPackage _package;
    private readonly DriveManager _driveManager;
    private readonly UploadLock _uploadLock;

    /// <summary />
    protected FileSystemStreamWriterBase(IDriveFileSystem fileSystem, TenantContext tenantContext, DotYouContextAccessor contextAccessor,
        DriveManager driveManager, UploadLock uploadLock)
    {
        FileSystem = fileSystem;

        _tenantContext = tenantContext;
        _contextAccessor = contextAccessor;
        _driveManager = driveManager;
        _uploadLock = uploadLock;
    }

    protected IDriveFileSystem FileSystem { get; }

    public virtual async Task<Guid> CreatePackage(UploadInstructionSet instructionSet)
    {
        Guard.Argument(instructionSet, nameof(instructionSet)).NotNull();
        instructionSet?.AssertIsValid();

        if (instructionSet!.TransitOptions?.Recipients?.Contains(_tenantContext.HostOdinId) ?? false)
        {
            throw new YouverseClientException("Cannot transfer to yourself; what's the point?", YouverseClientErrorCode.InvalidRecipient);
        }

        InternalDriveFileId file;
        var driveId = _driveManager.GetDriveIdByAlias(instructionSet!.StorageOptions!.Drive, true).Result.GetValueOrDefault();
        var overwriteFileId = instructionSet?.StorageOptions?.OverwriteFileId.GetValueOrDefault() ?? Guid.Empty;

        bool isUpdateOperation = false;

        if (overwriteFileId == Guid.Empty)
        {
            //get a new fileid
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

        var key = new Guid(ByteArrayUtil.EquiByteArrayXor(file.FileId.ToByteArray(), file.DriveId.ToByteArray()));
        if (!_uploadLock.Locks.TryAdd(key, file.FileId))
        {
            throw new YouverseClientException("File is locked", YouverseClientErrorCode.UploadedFileLocked);
        }

        var pkgId = Guid.NewGuid();
        this._package = new UploadPackage(pkgId, file, instructionSet!, isUpdateOperation);

        return await Task.FromResult(pkgId);
    }

    public virtual async Task<Guid> CreatePackageFromInstructionSet(Stream data)
    {
        //TODO: need to partially encrypt upload instruction set
        string json = await new StreamReader(data).ReadToEndAsync();
        var instructionSet = DotYouSystemSerializer.Deserialize<UploadInstructionSet>(json);

        return await this.CreatePackage(instructionSet);
    }

    public virtual async Task AddMetadata(Stream data)
    {
        await FileSystem.Storage.WriteTempStream(_package.InternalFile, MultipartUploadParts.Metadata.ToString(), data);
    }

    public virtual async Task AddPayload(Stream data)
    {
        var bytesWritten = await FileSystem.Storage.WriteTempStream(_package.InternalFile, MultipartUploadParts.Payload.ToString(), data);
        _package.HasPayload = bytesWritten > 0;
    }

    public virtual async Task AddThumbnail(int width, int height, string contentType, Stream data)
    {
        //TODO: How to store the content type for later usage?  is it even needed?

        //TODO: should i validate width and height are > 0?
        string extenstion = FileSystem.Storage.GetThumbnailFileExtension(width, height);
        await FileSystem.Storage.WriteTempStream(_package.InternalFile, extenstion, data);
    }

    public virtual async Task<UploadPackage> GetPackage()
    {
        return await Task.FromResult<UploadPackage>(_package);
    }

    /// <summary>
    /// Processes the instruction set on the specified packaged.  Used when all parts have been uploaded.
    /// </summary>
    public async Task<UploadResult> FinalizeUpload(Guid packageId)
    {

        var (keyHeader, metadata, serverMetadata) = await UnpackMetadata(_package);

        await this.ValidateUploadCore(_package, keyHeader, metadata, serverMetadata);

        await this.ValidateUnpackedData(_package, keyHeader, metadata, serverMetadata);

        if (_package.IsUpdateOperation)
        {
            // Validate the file exists by the Id
            if (!FileSystem.Storage.FileExists(_package.InternalFile))
            {
                throw new YouverseClientException("OverwriteFileId is specified but file does not exist",
                    YouverseClientErrorCode.CannotOverwriteNonExistentFile);
            }

            // If the uniqueId is being changed, validate that uniqueId is not in use by another file
            if (metadata.AppData.UniqueId.HasValue)
            {
                var incomingClientUniqueId = metadata.AppData.UniqueId.Value;
                var existingFileHeader = await FileSystem.Storage.GetServerFileHeader(_package.InternalFile);

                var isChangingUniqueId = incomingClientUniqueId != existingFileHeader.FileMetadata.AppData.UniqueId;
                if (isChangingUniqueId)
                {
                    var existingFile = await FileSystem.Query.GetFileByClientUniqueId(_package.InternalFile.DriveId, incomingClientUniqueId);
                    if (null != existingFile && existingFile.FileId != existingFileHeader.FileMetadata.File.FileId)
                    {
                        throw new YouverseClientException(
                            $"It looks like the uniqueId is being changed but a file already exists with ClientUniqueId: [{incomingClientUniqueId}]",
                            YouverseClientErrorCode.ExistingFileWithUniqueId);
                    }
                }
            }

            await ProcessExistingFileUpload(_package, keyHeader, metadata, serverMetadata);
        }
        else
        {
            // New file with UniqueId must not exist
            if (metadata.AppData.UniqueId.HasValue)
            {
                var incomingClientUniqueId = metadata.AppData.UniqueId.Value;
                var existingFile = await FileSystem.Query.GetFileByClientUniqueId(_package.InternalFile.DriveId, incomingClientUniqueId);
                if (null != existingFile)
                {
                    throw new YouverseClientException($"File already exists with ClientUniqueId: [{incomingClientUniqueId}]",
                        YouverseClientErrorCode.ExistingFileWithUniqueId);
                }
            }

            await ProcessNewFileUpload(_package, keyHeader, metadata, serverMetadata);
        }

        var key = new Guid(ByteArrayUtil.EquiByteArrayXor(metadata.File.FileId.ToByteArray(), metadata.File.DriveId.ToByteArray()));
        _uploadLock.Locks.TryRemove(key, out var _);

        Dictionary<string, TransferStatus> recipientStatus = await ProcessTransitInstructions(_package);

        var uploadResult = new UploadResult()
        {
            File = new ExternalFileIdentifier()
            {
                TargetDrive = _driveManager.GetDrive(_package.InternalFile.DriveId).Result.TargetDriveInfo,
                FileId = _package.InternalFile.FileId
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

        var json = global::System.Text.Encoding.UTF8.GetString(decryptedJsonBytes);

        var uploadDescriptor = DotYouSystemSerializer.Deserialize<UploadFileDescriptor>(json);

        var transferKeyEncryptedKeyHeader = uploadDescriptor!.EncryptedKeyHeader;

        if (null == transferKeyEncryptedKeyHeader)
        {
            throw new YouverseClientException("Failure to unpack upload metadata, invalid transfer key header", YouverseClientErrorCode.InvalidKeyHeader);
        }

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

    /// <summary>
    /// Validates rules that apply to all files; regardless of being feedback, normal, or some other type we've not yet conceived
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
            throw new YouverseClientException("Cannot upload an encrypted file that is accessible to anonymous visitors",
                YouverseClientErrorCode.CannotUploadEncryptedFileForAnonymous);
        }

        if (serverMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Authenticated && metadata.PayloadIsEncrypted)
        {
            throw new YouverseClientException("Cannot upload an encrypted file that is accessible to authenticated visitors",
                YouverseClientErrorCode.CannotUploadEncryptedFileForAnonymous);
        }

        if (metadata.AppData.ContentIsComplete && package.HasPayload)
        {
            throw new YouverseClientException("Content is marked complete in metadata but there is also a payload", YouverseClientErrorCode.InvalidPayload);
        }

        if (metadata.AppData.ContentIsComplete == false && package.HasPayload == false)
        {
            throw new YouverseClientException("Content is marked incomplete yet there is no payload", YouverseClientErrorCode.InvalidPayload);
        }

        var drive = await _driveManager.GetDrive(package.InternalFile.DriveId, true);
        if (drive.OwnerOnly && serverMetadata.AccessControlList.RequiredSecurityGroup != SecurityGroupType.Owner)
        {
            throw new YouverseClientException("Drive is owner only so all files must have RequiredSecurityGroup of Owner",
                YouverseClientErrorCode.DriveSecurityAndAclMismatch);
        }

        if (metadata.PayloadIsEncrypted)
        {
            if (ByteArrayUtil.IsStrongKey(keyHeader.Iv) == false || ByteArrayUtil.IsStrongKey(keyHeader.AesKey.GetKey()) == false)
            {
                throw new YouverseClientException("Payload is set as encrypted but the encryption key is too simple",
                    code: YouverseClientErrorCode.InvalidKeyHeader);
            }
        }

        //if a new file, we need to ensure the global transit is set correct.  for existing files, the system
        // uses the existing global transit id
        if (!package.IsUpdateOperation)
        {
            bool usesGlobalTransitId = package.InstructionSet.TransitOptions?.UseGlobalTransitId ?? false;
            if (serverMetadata.AllowDistribution && usesGlobalTransitId == false)
            {
                throw new YouverseClientException(
                    "UseGlobalTransitId must be true when AllowDistribution is true. (Yes, yes I know, i could just do it for you but then you would be all - htf is this GlobalTransitId getting set.. ooommmggg?!  Then you would hunt through the code and we would end up with long debate in the issue list.  #aintnobodygottimeforthat <3.  just love me and set the param",
                    YouverseClientErrorCode.InvalidTransitOptions);
            }
        }
    }
}