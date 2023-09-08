using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;

namespace Odin.Core.Services.Drives.FileSystem.Comment;

/// <summary />
public class CommentStreamWriter : FileSystemStreamWriterBase
{
    private readonly OdinContextAccessor _contextAccessor;
    private readonly ITransitService _transitService;

    /// <summary />
    public CommentStreamWriter(
        CommentFileSystem fileSystem,
        TenantContext tenantContext,
        OdinContextAccessor contextAccessor,
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
            throw new OdinClientException("GroupId is reserved for Text Reactions",
                OdinClientErrorCode.CannotUseGroupIdInTextReactions);
        }

        if (!(uploadDescriptor.FileMetadata.ReferencedFile?.HasValue() ?? false))
        {
            throw new OdinClientException(
                $"{nameof(uploadDescriptor.FileMetadata.ReferencedFile)} must be set and point to another file on the same drive",
                OdinClientErrorCode.InvalidReferenceFile);
        }

        return Task.CompletedTask;
    }

    protected override Task ValidateUnpackedData(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata)
    {
        return Task.CompletedTask;
    }

    protected override async Task ProcessNewFileUpload(UploadPackage package, KeyHeader keyHeader,
        FileMetadata metadata, ServerMetadata serverMetadata)
    {
        //
        // Note: this new file is a new comment but not a new ReferenceToFile; at
        // this point, we have validated the ReferenceToFile already exists
        //

        await FileSystem.Storage.CommitNewFile(package.InternalFile, keyHeader, metadata, serverMetadata);
    }

    protected override async Task ProcessExistingFileUpload(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        //target is same file because it's set earlier in the upload process
        //using overwrite here so we can ensure the right event is called
        var targetFile = package.InternalFile;

        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.MetadataOnly)
        {
            await FileSystem.Storage.OverwriteMetadata(
                targetFile: targetFile,
                newMetadata: metadata,
                newServerMetadata: serverMetadata);

            return;
        }

        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.NewFileOrOverwrite)
        {
            await FileSystem.Storage.OverwriteFile(tempFile: package.InternalFile,
                targetFile: targetFile,
                keyHeader: keyHeader,
                newMetadata: metadata,
                serverMetadata: serverMetadata);

            return;
        }

        throw new OdinSystemException("Unhandled Storage Intent");
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
            SenderOdinId = _contextAccessor.GetCurrent().Caller.OdinId,

            VersionTag = uploadDescriptor.FileMetadata.VersionTag
        };

        return Task.FromResult(metadata);
    }
}