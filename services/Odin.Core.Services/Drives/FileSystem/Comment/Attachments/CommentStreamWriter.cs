using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload.Attachments;

namespace Youverse.Core.Services.Drives.FileSystem.Comment.Attachments;

/// <summary />
public class CommentAttachmentStreamWriter : AttachmentStreamWriterBase
{
    /// <summary />
    public CommentAttachmentStreamWriter(
        CommentFileSystem fileSystem,
        OdinContextAccessor contextAccessor)
        : base(fileSystem, contextAccessor)
    {
    }

    protected override Task ValidateAttachments(AttachmentPackage package, ServerFileHeader header)
    {
        return Task.CompletedTask;
    }

    protected override async Task<Guid> UpdateAttachments(AttachmentPackage package, ServerFileHeader header)
    {
        return await FileSystem.Storage.UpdateAttachments(package.InternalFile,
            targetFile: package.InternalFile,
            incomingThumbnails: package.InstructionSet.Thumbnails);
    }
}