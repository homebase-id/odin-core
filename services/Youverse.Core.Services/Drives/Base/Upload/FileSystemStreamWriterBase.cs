using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Comment;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.Base.Upload;

namespace Youverse.Core.Services.Drives.Base.Upload;

/// <summary>
/// Enables the writing of file streams from external sources and
/// rule enforcement specific to the type of file system
/// </summary>
public abstract class FileSystemStreamWriterBase //<TFileSystem>
    // where TFileSystem : IDriveFileSystem
{
    private readonly TenantContext _tenantContext;
    private readonly DotYouContextAccessor _contextAccessor;

    private readonly ConcurrentDictionary<Guid, UploadPackage> _packages;
    private readonly DriveManager _driveManager;

    /// <summary />
    protected FileSystemStreamWriterBase(IDriveFileSystem fileSystem, TenantContext tenantContext, DotYouContextAccessor contextAccessor, DriveManager driveManager)
    {
        FileSystem = fileSystem;

        _tenantContext = tenantContext;
        _contextAccessor = contextAccessor;
        _driveManager = driveManager;
        _packages = new ConcurrentDictionary<Guid, UploadPackage>();
    }

    protected IDriveFileSystem FileSystem { get; }

    protected UploadPackage Package { get; }

    public virtual async Task<Guid> CreatePackage(Stream data)
    {
        //TODO: need to partially encrypt upload instruction set
        string json = await new StreamReader(data).ReadToEndAsync();
        var instructionSet = DotYouSystemSerializer.Deserialize<UploadInstructionSet>(json);

        Guard.Argument(instructionSet, nameof(instructionSet)).NotNull();
        instructionSet?.AssertIsValid();

        if (instructionSet!.TransitOptions?.Recipients?.Contains(_tenantContext.HostDotYouId) ?? false)
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

        var pkgId = Guid.NewGuid();
        var package = new UploadPackage(pkgId, file, instructionSet!, isUpdateOperation);

        if (!_packages.TryAdd(pkgId, package))
        {
            throw new YouverseSystemException("Failed to add the upload package");
        }

        return pkgId;
    }

    public virtual async Task AddMetadata(Guid packageId, Stream data)
    {
        if (!_packages.TryGetValue(packageId, out var pkg))
        {
            throw new YouverseSystemException("Invalid package Id");
        }

        await FileSystem.Storage.WriteTempStream(pkg.InternalFile, MultipartUploadParts.Metadata.ToString(), data);
    }

    public virtual async Task AddPayload(Guid packageId, Stream data)
    {
        if (!_packages.TryGetValue(packageId, out var pkg))
        {
            throw new YouverseSystemException("Invalid package Id");
        }

        var bytesWritten = await FileSystem.Storage.WriteTempStream(pkg.InternalFile, MultipartUploadParts.Payload.ToString(), data);
        var package = await this.GetPackage(packageId);
        package.HasPayload = bytesWritten > 0;
    }

    public virtual async Task AddThumbnail(Guid packageId, int width, int height, string contentType, Stream data)
    {
        if (!_packages.TryGetValue(packageId, out var pkg))
        {
            throw new YouverseSystemException("Invalid package Id");
        }

        //TODO: How to store the content type for later usage?  is it even needed?

        //TODO: should i validate width and height are > 0?
        string extenstion = FileSystem.Storage.GetThumbnailFileExtension(width, height);
        await FileSystem.Storage.WriteTempStream(pkg.InternalFile, extenstion, data);
    }

    public virtual async Task<UploadPackage> GetPackage(Guid packageId)
    {
        if (_packages.TryGetValue(packageId, out var package))
        {
            return package;
        }

        return null;
    }

    /// <summary>
    /// Processes the instruction set on the specified packaged.  Used when all parts have been uploaded.
    /// </summary>
    public async Task<UploadResult> FinalizeUpload(Guid packageId)
    {
        var package = await this.GetPackage(packageId);

        var (keyHeader, metadata, serverMetadata) = await UnpackMetadata(package);

        await this.ValidateUploadCore(package, keyHeader, metadata, serverMetadata);

        await this.ValidateUnpackedData(package, keyHeader, metadata, serverMetadata);

        if (package.IsUpdateOperation)
        {
            // Validate the file exists by the Id
            if (!FileSystem.Storage.FileExists(package.InternalFile))
            {
                throw new YouverseClientException("OverwriteFileId is specified but file does not exist", YouverseClientErrorCode.CannotOverwriteNonExistentFile);
            }

            // If the uniqueId is being changed, validate that uniqueId is not in use by another file
            if (metadata.AppData.UniqueId.HasValue)
            {
                var incomingClientUniqueId = metadata.AppData.UniqueId.Value;
                var existingFileHeader = await FileSystem.Storage.GetServerFileHeader(package.InternalFile);

                var isChangingUniqueId = incomingClientUniqueId != existingFileHeader.FileMetadata.AppData.UniqueId;
                if (isChangingUniqueId)
                {
                    var existingFile = await FileSystem.Query.GetFileByClientUniqueId(package.InternalFile.DriveId, incomingClientUniqueId);
                    if (null != existingFile && existingFile.FileId != existingFileHeader.FileMetadata.File.FileId)
                    {
                        throw new YouverseClientException($"It looks like the uniqueId is being changed but a file already exists with ClientUniqueId: [{incomingClientUniqueId}]",
                            YouverseClientErrorCode.ExistingFileWithUniqueId);
                    }
                }
            }

            await ProcessExistingFileUpload(package, keyHeader, metadata, serverMetadata);
        }
        else
        {
            // New file with UniqueId must not exist
            if (metadata.AppData.UniqueId.HasValue)
            {
                var incomingClientUniqueId = metadata.AppData.UniqueId.Value;
                var existingFile = await FileSystem.Query.GetFileByClientUniqueId(package.InternalFile.DriveId, incomingClientUniqueId);
                if (null != existingFile)
                {
                    throw new YouverseClientException($"File already exists with ClientUniqueId: [{incomingClientUniqueId}]", YouverseClientErrorCode.ExistingFileWithUniqueId);
                }
            }

            await ProcessNewFileUpload(package, keyHeader, metadata, serverMetadata);
        }

        Dictionary<string, TransferStatus> recipientStatus = await ProcessTransitInstructions(package);

        var uploadResult = new UploadResult()
        {
            File = new ExternalFileIdentifier()
            {
                TargetDrive = _driveManager.GetDrive(package.InternalFile.DriveId).Result.TargetDriveInfo,
                FileId = package.InternalFile.FileId
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

        var transferEncryptedKeyHeader = uploadDescriptor!.EncryptedKeyHeader;

        if (null == transferEncryptedKeyHeader)
        {
            throw new YouverseClientException("Failure to unpack upload metadata, invalid transfer key header", YouverseClientErrorCode.InvalidKeyHeader);
        }

        KeyHeader keyHeader = uploadDescriptor.FileMetadata.PayloadIsEncrypted ? transferEncryptedKeyHeader.DecryptAesToKeyHeader(ref clientSharedSecret) : KeyHeader.Empty();

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
            throw new YouverseClientException("Cannot upload an encrypted file that is accessible to anonymous visitors", YouverseClientErrorCode.CannotUploadEncryptedFileForAnonymous);
        }

        if (serverMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Authenticated && metadata.PayloadIsEncrypted)
        {
            throw new YouverseClientException("Cannot upload an encrypted file that is accessible to authenticated visitors", YouverseClientErrorCode.CannotUploadEncryptedFileForAnonymous);
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
            throw new YouverseClientException("Drive is owner only so all files must have RequiredSecurityGroup of Owner", YouverseClientErrorCode.DriveSecurityAndAclMismatch);
        }

        if (metadata.PayloadIsEncrypted)
        {
            if (ByteArrayUtil.IsStrongKey(keyHeader.Iv) == false || ByteArrayUtil.IsStrongKey(keyHeader.AesKey.GetKey()) == false)
            {
                throw new YouverseClientException("Payload is set as encrypted but the encryption key is too simple", code: YouverseClientErrorCode.InvalidKeyHeader);
            }
        }
    }
}