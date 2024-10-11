using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.FileSystem.V2.Upload.AddNew;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using UploadFileDescriptor = Odin.Services.Drives.FileSystem.Base.Upload.UploadFileDescriptor;

namespace Odin.Services.Drives.FileSystem.V2.Comment;

/// <summary />
public class CommentStreamWriterV2 : FileSystemStreamWriterBaseV2
{
    /// <summary />
    public CommentStreamWriterV2(
        CommentFileSystem fileSystem,
        TenantContext tenantContext,
        IPeerOutgoingTransferService peerOutgoingTransferService,
        DriveManager driveManager)
        : base(fileSystem, tenantContext, driveManager, peerOutgoingTransferService)
    {
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

    protected override Task ValidateUnpackedData(FileUploadPackageV2 package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }

    protected override async Task ProcessNewFileUpload(FileUploadPackageV2 package, KeyHeader keyHeader,
        FileMetadata metadata, ServerMetadata serverMetadata, IOdinContext odinContext, DatabaseConnection cn)
    {
        //
        // Note: this new file is a new comment but not a new ReferenceToFile; at
        // this point, we have validated the ReferenceToFile already exists
        //

        await FileSystem.Storage.CommitNewFile(package.InternalFile, keyHeader, metadata, serverMetadata, false, odinContext, cn);
    }

    protected override async Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(FileUploadPackageV2 package, IOdinContext odinContext,
        DatabaseConnection cn)
    {
        return await ProcessTransitBasic(package, FileSystemType.Comment, odinContext, cn);
    }

    protected override Task<FileMetadata> MapUploadToMetadata(FileUploadPackageV2 package,
        UploadFileDescriptor uploadDescriptor, IOdinContext odinContext)
    {
        var metadata = new FileMetadata()
        {
            File = package.InternalFile,
            GlobalTransitId = Guid.NewGuid(),

            ReferencedFile = uploadDescriptor.FileMetadata.ReferencedFile,

            //TODO: need an automapper *sigh
            AppData = new AppFileMetaData()
            {
                UniqueId = uploadDescriptor.FileMetadata.AppData.UniqueId,
                FileType = uploadDescriptor.FileMetadata.AppData.FileType,
                DataType = uploadDescriptor.FileMetadata.AppData.DataType,
                UserDate = uploadDescriptor.FileMetadata.AppData.UserDate,
                Tags = uploadDescriptor.FileMetadata.AppData.Tags,
                Content = uploadDescriptor.FileMetadata.AppData.Content,
                PreviewThumbnail = uploadDescriptor.FileMetadata.AppData.PreviewThumbnail,
                ArchivalStatus = uploadDescriptor.FileMetadata.AppData.ArchivalStatus,

                //Hijack the groupId by setting it to referenced file so the feed app can query by this transitId
                GroupId = uploadDescriptor.FileMetadata.ReferencedFile.GlobalTransitId,
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