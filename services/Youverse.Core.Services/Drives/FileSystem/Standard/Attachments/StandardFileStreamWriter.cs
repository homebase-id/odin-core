using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload.Attachments;
using Youverse.Core.Services.Drives.Management;

namespace Youverse.Core.Services.Drives.FileSystem.Standard.Attachments;

/// <summary />
public class StandardFileAttachmentStreamWriter : AttachmentStreamWriterBase
{
    /// <summary />
    public StandardFileAttachmentStreamWriter(StandardFileSystem fileSystem, DotYouContextAccessor contextAccessor, DriveManager driveManager)
        : base(fileSystem, contextAccessor, driveManager)
    {
    }

    protected override Task ValidateAttachments(AttachmentPackage package, ServerFileHeader header)
    {
        throw new NotImplementedException();
    }

    protected override async Task<Guid> UpdateAttachments(AttachmentPackage package, ServerFileHeader header)
    {
        //store the new attachments on disk
        
        
        return await FileSystem.Storage.UpdateAttachments(
            package.InternalFile, 
            targetFile: package.InternalFile,
            incomingThumbnails: package.InstructionSet.Thumbnails);
    }
}