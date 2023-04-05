using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Drives.FileSystem.Comment;

/// <summary />
public class CommentStreamWriter : FileSystemStreamWriterBase
{
    private readonly DotYouContextAccessor _contextAccessor;
    private readonly ITransitService _transitService;

    /// <summary />
    public CommentStreamWriter(
        CommentFileSystem fileSystem,
        TenantContext tenantContext,
        DotYouContextAccessor contextAccessor,
        ITransitService transitService,
        DriveManager driveManager)
        : base(fileSystem, tenantContext, contextAccessor, driveManager)
    {
        _contextAccessor = contextAccessor;
        _transitService = transitService;
    }

    protected override Task ValidateUploadDescriptor(UploadFileDescriptor uploadDescriptor)
    {
        //TODO: add other rules
        // - is the sender editing their own file?  if not, fail
        // - is the owner trying to edit a file? fail

        //enforce the drive permissions at this level?
        if (uploadDescriptor.FileMetadata.AppData.GroupId.HasValue)
        {
            throw new YouverseClientException("GroupId is reserved for Text Reactions",
                YouverseClientErrorCode.CannotUseGroupIdInTextReactions);
        }

        if (!(uploadDescriptor.FileMetadata.ReferencedFile?.HasValue() ?? false))
        {
            throw new YouverseClientException(
                $"{nameof(uploadDescriptor.FileMetadata.ReferencedFile)} must be set and point to another file on the same drive",
                YouverseClientErrorCode.InvalidReferenceFile);
        }

        return Task.CompletedTask;
    }

    protected override Task ValidateUnpackedData(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata)
    {
        // var referenceFileDriveId = _contextAccessor.GetCurrent().PermissionsContext
        //     .GetDriveId(metadata.ReferencedFile!.TargetDrive);
        //
        // var referenceFileInternal = new InternalDriveFileId()
        // {
        //     DriveId = referenceFileDriveId,
        //     FileId = metadata.ReferencedFile.GlobalTransitId
        // };

        //TODO: I removed this feature as more research is required
        // i.e. the file being targeted might be across file systems, etc.
        // so more must be done here at the file system level to query across them
        
        // if (!package.InstructionSet.StorageOptions.IgnoreMissingReferencedFile)
        // {
        //     var targetFile = FileSystem.Query.GetFileByGlobalTransitId(referenceFileInternal.DriveId,
        //         referenceFileInternal.FileId).GetAwaiter().GetResult();
        //
        //     if (null == targetFile || metadata.File.DriveId != referenceFileDriveId)
        //     {
        //         throw new YouverseClientException(
        //             "The referenced file must exist and be on the same drive as this file",
        //             YouverseClientErrorCode.InvalidReferenceFile);
        //     }
        // }

        return Task.CompletedTask;
    }

    protected override async Task ProcessNewFileUpload(UploadPackage package, KeyHeader keyHeader,
        FileMetadata metadata, ServerMetadata serverMetadata)
    {
        //
        // Note: this new file is a new comment but not a new ReferenceToFile; at
        // this point, we have validated the ReferenceToFile already exists
        //

        await FileSystem.Storage.CommitNewFile(package.InternalFile, keyHeader, metadata, serverMetadata, "payload");
    }

    protected override async Task ProcessExistingFileUpload(UploadPackage package, KeyHeader keyHeader,
        FileMetadata metadata, ServerMetadata serverMetadata)
    {
        //target is same file because it's set earlier in the upload process
        //using overwrite here so we can ensure the right event is called
        var targetFile = package.InternalFile;
        await FileSystem.Storage.OverwriteFile(tempFile: package.InternalFile,
            targetFile: targetFile,
            keyHeader: keyHeader,
            metadata: metadata,
            serverMetadata: serverMetadata,
            "payload");
    }

    protected override async Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(UploadPackage package)
    {
        Dictionary<string, TransferStatus> recipientStatus = null;
        var recipients = package.InstructionSet.TransitOptions?.Recipients;
        if (recipients?.Any() ?? false)
        {
            recipientStatus = await _transitService.SendFile(package.InternalFile,
                package.InstructionSet.TransitOptions, TransferFileType.Normal, FileSystemType.Comment);
        }

        return recipientStatus;
    }

    protected override Task<FileMetadata> MapUploadToMetadata(UploadPackage package,
        UploadFileDescriptor uploadDescriptor)
    {
        var metadata = new FileMetadata()
        {
            File = package.InternalFile,
            GlobalTransitId = (package.InstructionSet.TransitOptions?.UseGlobalTransitId ?? false)
                ? Guid.NewGuid()
                : null,
            ContentType = uploadDescriptor.FileMetadata.ContentType,

            ReferencedFile = uploadDescriptor.FileMetadata.ReferencedFile,

            //TODO: need an automapper *sigh
            AppData = new AppFileMetaData()
            {
                UniqueId = uploadDescriptor.FileMetadata.AppData.UniqueId,
                FileType = uploadDescriptor.FileMetadata.AppData.FileType,
                DataType = uploadDescriptor.FileMetadata.AppData.DataType,
                UserDate = uploadDescriptor.FileMetadata.AppData.UserDate,
                Tags = uploadDescriptor.FileMetadata.AppData.Tags,
                JsonContent = uploadDescriptor.FileMetadata.AppData.JsonContent,
                ContentIsComplete = uploadDescriptor.FileMetadata.AppData.ContentIsComplete,
                PreviewThumbnail = uploadDescriptor.FileMetadata.AppData.PreviewThumbnail,
                AdditionalThumbnails = uploadDescriptor.FileMetadata.AppData.AdditionalThumbnails,
                ArchivalStatus = uploadDescriptor.FileMetadata.AppData.ArchivalStatus,

                //Hijack the groupId by setting it to referenced file
                GroupId = uploadDescriptor.FileMetadata.ReferencedFile.GlobalTransitId,
            },

            PayloadIsEncrypted = uploadDescriptor.FileMetadata.PayloadIsEncrypted,
            OriginalRecipientList = package.InstructionSet.TransitOptions?.Recipients,
            SenderOdinId = _contextAccessor.GetCurrent().Caller.OdinId
        };

        return Task.FromResult(metadata);
    }
}