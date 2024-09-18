using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Drives.FileSystem.Standard.Update;

/// <summary />
public class StandardFileUpdateWriter : FileSystemUpdateWriterBase
{
    /// <summary />
    public StandardFileUpdateWriter(StandardFileSystem fileSystem,
        PeerOutgoingTransferService peerOutgoingTransferService,
        DriveManager driveManager)
        : base(fileSystem, driveManager, peerOutgoingTransferService)
    {
    }

    protected override Task ValidateUploadDescriptor(UpdateFileDescriptor updateDescriptor)
    {
        if (ReservedFileTypes.IsInReservedRange(updateDescriptor.FileMetadata.AppData.FileType))
        {
            throw new OdinClientException($"Cannot upload file with reserved file type; range is {ReservedFileTypes.Start} - {ReservedFileTypes.End}",
                OdinClientErrorCode.CannotUseReservedFileType);
        }

        if (updateDescriptor.FileMetadata.ReferencedFile?.HasValue() ?? false)
        {
            throw new OdinClientException($"{nameof(updateDescriptor.FileMetadata.ReferencedFile)} cannot be used with standard file types",
                OdinClientErrorCode.CannotUseReferencedFileOnStandardFiles);
        }

        return Task.CompletedTask;
    }

   
    protected override Task<FileMetadata> MapUploadToMetadata(FileUpdatePackage package, UpdateFileDescriptor updateDescriptor, IOdinContext odinContext)
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
                UniqueId = updateDescriptor.FileMetadata.AppData.UniqueId,
                Tags = updateDescriptor.FileMetadata.AppData.Tags,
                FileType = updateDescriptor.FileMetadata.AppData.FileType,
                DataType = updateDescriptor.FileMetadata.AppData.DataType,
                UserDate = updateDescriptor.FileMetadata.AppData.UserDate,
                GroupId = updateDescriptor.FileMetadata.AppData.GroupId,
                ArchivalStatus = updateDescriptor.FileMetadata.AppData.ArchivalStatus,
                Content = updateDescriptor.FileMetadata.AppData.Content,
                PreviewThumbnail = updateDescriptor.FileMetadata.AppData.PreviewThumbnail
            },

            IsEncrypted = updateDescriptor.FileMetadata.IsEncrypted,
            SenderOdinId = "", //Note: in this case, this is who uploaded the file therefore should be empty; until we support youauth uploads

            VersionTag = updateDescriptor.FileMetadata.VersionTag,

            Payloads = package.GetFinalPayloadDescriptors()
        };

        return Task.FromResult(metadata);
    }
}