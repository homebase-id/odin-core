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

namespace Odin.Services.Drives.FileSystem.Comment.Update;

/// <summary />
public class CommentFileUpdateWriter : FileSystemUpdateWriterBase
{
    /// <summary />
    public CommentFileUpdateWriter(
        CommentFileSystem fileSystem,
        PeerOutgoingTransferService peerOutgoingTransferService,
        DriveManager driveManager)
        : base(fileSystem, driveManager, peerOutgoingTransferService)
    {
    }

    protected override Task ValidateUploadDescriptor(UpdateFileDescriptor updateDescriptor)
    {
        //TODO: add other rules
        // - is the sender editing their own file?  if not, fail
        // - is the owner trying to edit a file? fail

        //enforce the drive permissions at this level?
        if (updateDescriptor.FileMetadata.AppData.GroupId.HasValue)
        {
            throw new OdinClientException("GroupId is reserved for Text Reactions",
                OdinClientErrorCode.CannotUseGroupIdInTextReactions);
        }

        if (!(updateDescriptor.FileMetadata.ReferencedFile?.HasValue() ?? false))
        {
            throw new OdinClientException(
                $"{nameof(updateDescriptor.FileMetadata.ReferencedFile)} must be set and point to another file on the same drive",
                OdinClientErrorCode.InvalidReferenceFile);
        }

        return Task.CompletedTask;
    }
    
    protected override Task<FileMetadata> MapUploadToMetadata(FileUpdatePackage package,
        UpdateFileDescriptor updateDescriptor, IOdinContext odinContext)
    {
        var metadata = new FileMetadata()
        {
            File = package.InternalFile,
            GlobalTransitId = Guid.NewGuid(),

            ReferencedFile = updateDescriptor.FileMetadata.ReferencedFile,

            //TODO: need an automapper *sigh
            AppData = new AppFileMetaData()
            {
                UniqueId = updateDescriptor.FileMetadata.AppData.UniqueId,
                FileType = updateDescriptor.FileMetadata.AppData.FileType,
                DataType = updateDescriptor.FileMetadata.AppData.DataType,
                UserDate = updateDescriptor.FileMetadata.AppData.UserDate,
                Tags = updateDescriptor.FileMetadata.AppData.Tags,
                Content = updateDescriptor.FileMetadata.AppData.Content,
                PreviewThumbnail = updateDescriptor.FileMetadata.AppData.PreviewThumbnail,
                ArchivalStatus = updateDescriptor.FileMetadata.AppData.ArchivalStatus,

                //Hijack the groupId by setting it to referenced file so the feed app can query by this transitId
                GroupId = updateDescriptor.FileMetadata.ReferencedFile.GlobalTransitId,
            },

            IsEncrypted = updateDescriptor.FileMetadata.IsEncrypted,
            SenderOdinId = odinContext.Caller.OdinId,

            VersionTag = updateDescriptor.FileMetadata.VersionTag,

            Payloads = package.GetFinalPayloadDescriptors()
        };

        return Task.FromResult(metadata);
    }
}