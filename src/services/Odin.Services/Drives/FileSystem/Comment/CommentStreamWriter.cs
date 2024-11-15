using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Drives.FileSystem.Comment;

/// <summary />
public class CommentStreamWriter : FileSystemStreamWriterBase
{
    private readonly PeerOutgoingTransferService _peerOutgoingTransferService;

    /// <summary />
    public CommentStreamWriter(
        CommentFileSystem fileSystem,
        TenantContext tenantContext,
        PeerOutgoingTransferService peerOutgoingTransferService,
        DriveManager driveManager)
        : base(fileSystem, tenantContext, driveManager, peerOutgoingTransferService)
    {
        _peerOutgoingTransferService = peerOutgoingTransferService;
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

    protected override Task ValidateUnpackedData(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }

    protected override async Task ProcessNewFileUpload(FileUploadPackage package, KeyHeader keyHeader,
        FileMetadata metadata, ServerMetadata serverMetadata, IOdinContext odinContext, IdentityDatabase db)
    {
        //
        // Note: this new file is a new comment but not a new ReferenceToFile; at
        // this point, we have validated the ReferenceToFile already exists
        //

        await FileSystem.Storage.CommitNewFile(package.InternalFile, keyHeader, metadata, serverMetadata, false, odinContext, db);
    }

    protected override async Task ProcessExistingFileUpload(FileUploadPackage package, KeyHeader keyHeader, FileMetadata metadata,
        ServerMetadata serverMetadata, IOdinContext odinContext, IdentityDatabase db)
    {
        //target is same file because it's set earlier in the upload process
        //using overwrite here, so we can ensure the right event is called
        var targetFile = package.InternalFile;

        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.MetadataOnly)
        {
            await FileSystem.Storage.OverwriteMetadata(
                keyHeader.Iv,
                targetFile,
                metadata,
                serverMetadata,
                odinContext,
                db);

            return;
        }

        if (package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.NewFileOrOverwrite)
        {
            await FileSystem.Storage.OverwriteFile(tempFile: package.InternalFile,
                targetFile: targetFile,
                keyHeader: keyHeader,
                newMetadata: metadata,
                serverMetadata: serverMetadata,
                ignorePayload: false,
                odinContext: odinContext,
                db);

            return;
        }

        throw new OdinSystemException("Unhandled Storage Intent");
    }

    protected override async Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(FileUploadPackage package, IOdinContext odinContext,
        IdentityDatabase db)
    {
        return await ProcessTransitBasic(package, FileSystemType.Comment, odinContext, db);
    }

    protected override Task<FileMetadata> MapUploadToMetadata(FileUploadPackage package,
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