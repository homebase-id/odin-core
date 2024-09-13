using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Drives.FileSystem.Standard.Update;

/// <summary />
public class StandardFileUpdateWriter : FileSystemUpdateWriterBase
{
    /// <summary />
    public StandardFileUpdateWriter(StandardFileSystem fileSystem,
        IPeerOutgoingTransferService peerOutgoingTransferService,
        DriveManager driveManager)
        : base(fileSystem, driveManager, peerOutgoingTransferService)
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

    protected override async Task ProcessExistingFileUpload(FileUpdatePackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata, IOdinContext odinContext, DatabaseConnection cn)
    {
        //
        // Note: need to have just one version tag when all done
        //
        
        // Here we will examine the manifest; by adding / deleting payloads according
        // then overwrite the metadata

        var file = package.InternalFile;
        foreach (var descriptor in package.InstructionSet.Manifest.PayloadDescriptors ?? [])
        {
            var key = descriptor.PayloadKey;
            if (descriptor.FileUpdateOperationType == FileUpdateOperationType.AddPayload)
            {
                // FileSystem.Storage.DeletePayload(file, key, )
            }

            if (descriptor.FileUpdateOperationType == FileUpdateOperationType.DeletePayload)
            {
                var newVersionTag = await FileSystem.Storage.DeletePayload(file, descriptor.PayloadKey, metadata.VersionTag.GetValueOrDefault(),
                    odinContext, cn);
            }
        }

        // then save the metadata
        //
        // await FileSystem.Storage.OverwriteMetadata(
        //     keyHeader.Iv,
        //     targetFile: package.InternalFile,
        //     newMetadata: metadata,
        //     newServerMetadata: serverMetadata,
        //     odinContext: odinContext, cn);
        //
        //
        // await FileSystem.Storage.OverwriteFile(tempFile: package.InternalFile,
        //     targetFile: package.InternalFile,
        //     keyHeader: keyHeader,
        //     newMetadata: metadata,
        //     serverMetadata: serverMetadata,
        //     ignorePayload: false,
        //     odinContext: odinContext,
        //     cn);
    }

    protected override async Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(FileUpdatePackage package, IOdinContext odinContext,
        DatabaseConnection cn)
    {
        return await ProcessTransitBasic(package, FileSystemType.Standard, odinContext, cn);
    }

    protected override Task<FileMetadata> MapUploadToMetadata(FileUpdatePackage package, UploadFileDescriptor uploadDescriptor, IOdinContext odinContext)
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
            SenderOdinId = "", //Note: in this case, this is who uploaded the file therefore should be empty; until we support youauth uploads

            VersionTag = uploadDescriptor.FileMetadata.VersionTag,

            Payloads = package.GetFinalPayloadDescriptors()
        };

        return Task.FromResult(metadata);
    }
}