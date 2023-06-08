using System;
using System.Threading.Tasks;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload.Attachments;

namespace Odin.Core.Services.Drives.FileSystem.Comment.Attachments;

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