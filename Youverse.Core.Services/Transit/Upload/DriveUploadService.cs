using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit.Upload;

/// <summary>
/// Enables the uploading of files 
/// </summary>
public class DriveUploadService
{
    private readonly IDriveService _driveService;
    private readonly TenantContext _tenantContext;
    private readonly DotYouContextAccessor _contextAccessor;
    private readonly ITransitService _transitService;

    private readonly Dictionary<Guid, UploadPackage> _packages;
    private readonly IDriveQueryService _driveQueryService;

    public DriveUploadService(IDriveService driveService, TenantContext tenantContext, DotYouContextAccessor contextAccessor, ITransitService transitService, IDriveQueryService driveQueryService)
    {
        _driveService = driveService;
        _tenantContext = tenantContext;
        _contextAccessor = contextAccessor;
        _transitService = transitService;
        _driveQueryService = driveQueryService;

        _packages = new Dictionary<Guid, UploadPackage>();
    }

    /// <summary>
    /// Processes the instruction set on the specified packaged.  Used when all parts have been uploaded. 
    /// </summary>
    public async Task<UploadResult> FinalizeUpload(Guid packageId)
    {
        var package = await this.GetPackage(packageId);

        //TODO: need to handle when files are being updated.  remove old thumbnails, etc.

        if (package.InstructionSet.TransitOptions?.Recipients?.Contains(_tenantContext.HostDotYouId) ?? false)
        {
            throw new UploadException("Cannot transfer a file to the sender; what's the point?");
        }

        //TODO: this is pending.  for now, you upload a full set of streams (payload, thumbnail, etc.) to overwrite a file
        // if (package.IsUpdateOperation)
        // {
        //     return await ProcessUploadOfExistingFile(package);
        // }

        return await ProcessUpload(package);
    }

    public async Task<Guid> CreatePackage(Stream data)
    {
        //TODO: need to partially encrypt upload instruction set
        string json = await new StreamReader(data).ReadToEndAsync();
        var instructionSet = DotYouSystemSerializer.Deserialize<UploadInstructionSet>(json);

        Guard.Argument(instructionSet, nameof(instructionSet)).NotNull();
        instructionSet?.AssertIsValid();

        if (instructionSet!.TransitOptions?.Recipients?.Contains(_tenantContext.HostDotYouId) ?? false)
        {
            throw new UploadException("Cannot transfer to yourself; what's the point?");
        }

        InternalDriveFileId file;
        var driveId = _driveService.GetDriveIdByAlias(instructionSet!.StorageOptions!.Drive, true).Result.GetValueOrDefault();
        var overwriteFileId = instructionSet?.StorageOptions?.OverwriteFileId.GetValueOrDefault() ?? Guid.Empty;

        bool isUpdateOperation = false;

        if (overwriteFileId == Guid.Empty)
        {
            //get a new fileid
            file = _driveService.CreateInternalFileId(driveId);
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
        _packages.Add(pkgId, package);

        return pkgId;
    }

    public async Task AddMetadata(Guid packageId, Stream data)
    {
        if (!_packages.TryGetValue(packageId, out var pkg))
        {
            throw new UploadException("Invalid package Id");
        }

        await _driveService.WriteTempStream(pkg.InternalFile, MultipartUploadParts.Metadata.ToString(), data);
    }

    public async Task AddPayload(Guid packageId, Stream data)
    {
        if (!_packages.TryGetValue(packageId, out var pkg))
        {
            throw new UploadException("Invalid package Id");
        }

        await _driveService.WriteTempStream(pkg.InternalFile, MultipartUploadParts.Payload.ToString(), data);
    }

    public async Task AddThumbnail(Guid packageId, int width, int height, string contentType, Stream data)
    {
        if (!_packages.TryGetValue(packageId, out var pkg))
        {
            throw new UploadException("Invalid package Id");
        }

        //TODO: How to store the content type for later usage?  is it even needed?

        //TODO: should i validate width and height are > 0?
        string extenstion = _driveService.GetThumbnailFileExtension(width, height);
        await _driveService.WriteTempStream(pkg.InternalFile, extenstion, data);
    }

    public async Task<UploadPackage> GetPackage(Guid packageId)
    {
        if (_packages.TryGetValue(packageId, out var package))
        {
            return package;
        }

        return null;
    }

    private async Task<UploadResult> ProcessUpload(UploadPackage package)
    {
        var (keyHeader, metadata, serverMetadata) = await UnpackMetadata(package);
        if (null == serverMetadata.AccessControlList)
        {
            throw new MissingDataException("Access control list must be specified");
        }

        serverMetadata.AccessControlList.Validate();

        if (serverMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Anonymous && metadata.PayloadIsEncrypted)
        {
            //Note: dont allow anonymously accessible encrypted files because we wont have a client shared secret to secure the key header
            throw new UploadException("Cannot upload an encrypted file that is accessible to anonymous visitors");
        }

        var drive = await _driveService.GetDrive(package.InternalFile.DriveId, true);
        if (drive.OwnerOnly && serverMetadata.AccessControlList.RequiredSecurityGroup != SecurityGroupType.Owner)
        {
            throw new UploadException("Drive is owner only so all files must have RequiredSecurityGroup of Owner");
        }

        if (package.IsUpdateOperation)
        {
            //validate the file exists by the ID
            if (!_driveService.FileExists(package.InternalFile))
            {
                throw new UploadException("OverwriteFileId is specified but file does not exist");
            }

            if (metadata.AppData.ClientUniqueId.HasValue)
            {
                var incomingClientUniqueId = metadata.AppData.ClientUniqueId.Value;
                var existingFileHeader = await _driveService.GetServerFileHeader(package.InternalFile);

                var isChangingUniqueId = incomingClientUniqueId != existingFileHeader.FileMetadata.AppData.ClientUniqueId;
                if (isChangingUniqueId)
                {
                    var existingFile = await _driveQueryService.GetFileByClientUniqueId(package.InternalFile.DriveId, incomingClientUniqueId);
                    if (null != existingFile && existingFile.FileId != existingFileHeader.FileMetadata.File.FileId)
                    {
                        throw new UploadException($"File already exists with ClientUniqueId: [{incomingClientUniqueId}]");
                    }
                }
            }
        }

        await _driveService.CommitTempFileToLongTerm(package.InternalFile, keyHeader, metadata, serverMetadata, MultipartUploadParts.Payload.ToString());

        Dictionary<string, TransferStatus> recipientStatus = null;
        var recipients = package.InstructionSet.TransitOptions?.Recipients;
        if (recipients?.Any() ?? false)
        {
            recipientStatus = await _transitService.SendFile(package.InternalFile, package.InstructionSet.TransitOptions);
        }

        var uploadResult = new UploadResult()
        {
            File = new ExternalFileIdentifier()
            {
                TargetDrive = _driveService.GetDrive(package.InternalFile.DriveId).Result.TargetDriveInfo,
                FileId = package.InternalFile.FileId
            },
            GlobalTransitId = metadata.GlobalTransitId,
            RecipientStatus = recipientStatus
        };

        return uploadResult;
    }

    private async Task<UploadResult> ProcessUploadOfExistingFile(UploadPackage package)
    {
        throw new NotImplementedException("Partial updates for files are not currently supported");
    }

    private async Task<(KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)> UnpackMetadata(UploadPackage package)
    {
        var metadataStream = await _driveService.GetTempStream(package.InternalFile, MultipartUploadParts.Metadata.ToString());

        var clientSharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
        var jsonBytes = AesCbc.Decrypt(metadataStream.ToByteArray(), ref clientSharedSecret, package.InstructionSet.TransferIv);
        var json = System.Text.Encoding.UTF8.GetString(jsonBytes);

        var uploadDescriptor = DotYouSystemSerializer.Deserialize<UploadFileDescriptor>(json);

        var transferEncryptedKeyHeader = uploadDescriptor!.EncryptedKeyHeader;

        if (null == transferEncryptedKeyHeader)
        {
            throw new UploadException("Invalid transfer key header");
        }

        KeyHeader keyHeader = uploadDescriptor.FileMetadata.PayloadIsEncrypted ? transferEncryptedKeyHeader.DecryptAesToKeyHeader(ref clientSharedSecret) : KeyHeader.Empty();
        var metadata = new FileMetadata(package.InternalFile)
        {
            GlobalTransitId = (package.InstructionSet.TransitOptions?.UseGlobalTransitId ?? false) ? Guid.NewGuid() : null,
            ContentType = uploadDescriptor.FileMetadata.ContentType,

            //TODO: need an automapper *sigh
            AppData = new AppFileMetaData()
            {
                ClientUniqueId = uploadDescriptor.FileMetadata.AppData.ClientUniqueId,
                Tags = uploadDescriptor.FileMetadata.AppData.Tags,
                FileType = uploadDescriptor.FileMetadata.AppData.FileType,
                DataType = uploadDescriptor.FileMetadata.AppData.DataType,
                UserDate = uploadDescriptor.FileMetadata.AppData.UserDate,
                GroupId = uploadDescriptor.FileMetadata.AppData.GroupId,

                JsonContent = uploadDescriptor.FileMetadata.AppData.JsonContent,
                ContentIsComplete = uploadDescriptor.FileMetadata.AppData.ContentIsComplete,

                PreviewThumbnail = uploadDescriptor.FileMetadata.AppData.PreviewThumbnail,
                AdditionalThumbnails = uploadDescriptor.FileMetadata.AppData.AdditionalThumbnails
            },

            PayloadIsEncrypted = uploadDescriptor.FileMetadata.PayloadIsEncrypted,
            OriginalRecipientList = package.InstructionSet.TransitOptions?.Recipients,

            // SenderDotYouId = _contextAccessor.GetCurrent().Caller.DotYouId  
            SenderDotYouId = "" //Note: in this case, this is who uploaded the file therefore should be empty; until we support youauth uploads
        };

        var serverMetadata = new ServerMetadata()
        {
            AccessControlList = uploadDescriptor.FileMetadata.AccessControlList
        };

        return (keyHeader, metadata, serverMetadata);
    }
}