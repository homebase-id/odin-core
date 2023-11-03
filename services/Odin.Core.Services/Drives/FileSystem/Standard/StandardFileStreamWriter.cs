using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Services.Apps.CommandMessaging;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Core.Storage;

namespace Odin.Core.Services.Drives.FileSystem.Standard;

/// <summary />
public class StandardFileStreamWriter : FileSystemStreamWriterBase
{
    private readonly ITransitService _transitService;

    /// <summary />
    public StandardFileStreamWriter(StandardFileSystem fileSystem, TenantContext tenantContext, OdinContextAccessor contextAccessor,
        ITransitService transitService,
        DriveManager driveManager)
        : base(fileSystem, tenantContext, contextAccessor, driveManager)
    {
        _transitService = transitService;
    }

    protected override Task ValidateUploadDescriptor(UploadFileDescriptor uploadDescriptor)
    {
        if (ReservedFileTypes.IsInReservedRange(uploadDescriptor.FileMetadata.AppData.FileType))
        {
            throw new OdinClientException($"Cannot upload file with reserved file type; range is {ReservedFileTypes.Start} - {ReservedFileTypes.End}",
                OdinClientErrorCode.CannotUseReservedFileType);
        }

        if (uploadDescriptor.FileMetadata.ReferencedFile?.HasValue() ?? false)
        {
            throw new OdinClientException($"{nameof(uploadDescriptor.FileMetadata.ReferencedFile)} cannot be used with standard file types",
                OdinClientErrorCode.CannotUseReferencedFileOnStandardFiles);
        }

        return Task.CompletedTask;
    }

    protected override Task ValidateUnpackedData(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        return Task.CompletedTask;
    }

    protected override async Task ProcessNewFileUpload(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        await FileSystem.Storage.CommitNewFile(package.InternalFile, keyHeader, metadata, serverMetadata, false, false);
    }

    protected override async Task ProcessExistingFileUpload(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.MetadataOnly)
        {
            await FileSystem.Storage.OverwriteMetadata(
                targetFile: package.InternalFile,
                newMetadata: metadata,
                newServerMetadata: serverMetadata);

            return;
        }

        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.NewFileOrOverwrite)
        {
            await FileSystem.Storage.OverwriteFile(tempFile: package.InternalFile,
                targetFile: package.InternalFile,
                keyHeader: keyHeader,
                newMetadata: metadata,
                serverMetadata: serverMetadata,
                ignorePayload: false,
                ignoreThumbnail: false);

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
            recipientStatus = await _transitService.SendFile(package.InternalFile, package.InstructionSet.TransitOptions, TransferFileType.Normal,
                FileSystemType.Standard);
        }

        return recipientStatus;
    }

    protected override Task<FileMetadata> MapUploadToMetadata(UploadPackage package, UploadFileDescriptor uploadDescriptor)
    {
        var metadata = new FileMetadata()
        {
            File = package.InternalFile,
            GlobalTransitId = (package.InstructionSet.TransitOptions?.UseGlobalTransitId ?? false) ? Guid.NewGuid() : null,

            //Note: this intentionally does not map ReferenceToFile; this can only be done through the comment system
            // ReferencedFile = null,

            //TODO: need an automapper *sigh
            AppData = new AppFileMetaData()
            {
                UniqueId = uploadDescriptor.FileMetadata.AppData.UniqueId,
                Tags = uploadDescriptor.FileMetadata.AppData.Tags,
                FileType = uploadDescriptor.FileMetadata.AppData.FileType,
                DataType = uploadDescriptor.FileMetadata.AppData.DataType,
                UserDate = uploadDescriptor.FileMetadata.AppData.UserDate,
                GroupId = uploadDescriptor.FileMetadata.AppData.GroupId,
                ArchivalStatus = uploadDescriptor.FileMetadata.AppData.ArchivalStatus,
                Content = uploadDescriptor.FileMetadata.AppData.Content,
                PreviewThumbnail = uploadDescriptor.FileMetadata.AppData.PreviewThumbnail
            },

            IsEncrypted = uploadDescriptor.FileMetadata.IsEncrypted,
            SenderOdinId = "", //Note: in this case, this is who uploaded the file therefore should be empty; until we support youauth uploads

            VersionTag = uploadDescriptor.FileMetadata.VersionTag,

            Payloads = package.UploadedPayloads,
            Thumbnails = package.UploadedThumbnails
        };

        return Task.FromResult(metadata);
    }
}