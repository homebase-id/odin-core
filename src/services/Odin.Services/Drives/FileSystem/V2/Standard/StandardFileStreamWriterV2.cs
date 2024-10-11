using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.FileSystem.V2.Upload.AddNew;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using UploadFileDescriptor = Odin.Services.Drives.FileSystem.Base.Upload.UploadFileDescriptor;

namespace Odin.Services.Drives.FileSystem.V2.Standard;

/// <summary />
public class StandardFileStreamWriterV2 : FileSystemStreamWriterBaseV2
{
    /// <summary />
    public StandardFileStreamWriterV2(StandardFileSystem fileSystem, TenantContext tenantContext,
        IPeerOutgoingTransferService peerOutgoingTransferService,
        DriveManager driveManager)
        : base(fileSystem, tenantContext, driveManager, peerOutgoingTransferService)
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

    protected override Task ValidateUnpackedData(FileUploadPackageV2 package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata,
        IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }

    protected override async Task ProcessNewFileUpload(FileUploadPackageV2 package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata,
        IOdinContext odinContext, DatabaseConnection cn)
    {
        await FileSystem.Storage.CommitNewFile(package.InternalFile, keyHeader, metadata, serverMetadata, false, odinContext, cn);
    }
    
    protected override async Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(FileUploadPackageV2 package, IOdinContext odinContext, DatabaseConnection cn)
    {
        return await ProcessTransitBasic(package, FileSystemType.Standard, odinContext, cn);
    }

    protected override Task<FileMetadata> MapUploadToMetadata(FileUploadPackageV2 package, UploadFileDescriptor uploadDescriptor, IOdinContext odinContext)
    {
        var metadata = new FileMetadata()
        {
            File = package.InternalFile,
            GlobalTransitId = Guid.NewGuid(),

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
            SenderOdinId = odinContext.GetCallerOdinIdOrFail(),
            OriginalAuthor = odinContext.GetCallerOdinIdOrFail(),

            VersionTag = uploadDescriptor.FileMetadata.VersionTag,

            Payloads = package.GetFinalPayloadDescriptors()
        };

        return Task.FromResult(metadata);
    }
}