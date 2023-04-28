using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload.Attachments;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Transit.SendingHost;

namespace Youverse.Core.Services.Drives.FileSystem.Comment.Attachments;

/// <summary />
public class CommentAttachmentStreamWriter : AttachmentStreamWriterBase
{
    private readonly DotYouContextAccessor _contextAccessor;
    private readonly ITransitService _transitService;

    /// <summary />
    public CommentAttachmentStreamWriter(
        CommentFileSystem fileSystem,
        DotYouContextAccessor contextAccessor,
        ITransitService transitService,
        DriveManager driveManager)
        : base(fileSystem, contextAccessor, driveManager)
    {
        _contextAccessor = contextAccessor;
        _transitService = transitService;
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