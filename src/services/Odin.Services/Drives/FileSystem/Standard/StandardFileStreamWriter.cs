using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Services.Peer.Outgoing;
using Odin.Core.Storage;
using Odin.Services.Apps.CommandMessaging;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Drives.FileSystem.Standard;

/// <summary />
public class StandardFileStreamWriter : FileSystemStreamWriterBase
{
    /// <summary />
    public StandardFileStreamWriter(StandardFileSystem fileSystem, TenantContext tenantContext, OdinContextAccessor contextAccessor,
        IPeerOutgoingTransferService peerOutgoingTransferService,
        DriveManager driveManager)
        : base(fileSystem, tenantContext, contextAccessor, driveManager, peerOutgoingTransferService)
    {
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

    protected override Task ValidateUnpackedData(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        return Task.CompletedTask;
    }

    protected override async Task ProcessNewFileUpload(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        await FileSystem.Storage.CommitNewFile(package.InternalFile, keyHeader, metadata, serverMetadata, false);
    }

    protected override async Task ProcessExistingFileUpload(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata)
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
                ignorePayload: false);

            return;
        }

        throw new OdinSystemException("Unhandled Storage Intent");
    }

    protected override async Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(FileUploadPackage package)
    {
        return await ProcessTransitBasic(package, FileSystemType.Standard);
    }

    protected override Task<FileMetadata> MapUploadToMetadata(FileUploadPackage package, UploadFileDescriptor uploadDescriptor)
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

            Payloads = package.GetFinalPayloadDescriptors()
        };

        return Task.FromResult(metadata);
    }
}