using System;
using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;

namespace Odin.Services.Drives.FileSystem.Comment.Attachments;

/// <summary />
public class CommentPayloadStreamWriter : PayloadStreamWriterBase
{
    /// <summary />
    public CommentPayloadStreamWriter(
        CommentFileSystem fileSystem)
        : base(fileSystem)
    {
    }

    protected override Task ValidatePayloads(PayloadOnlyPackage package, ServerFileHeader header)
    {
        return Task.CompletedTask;
    }

    protected override async Task<Guid> UpdatePayloads(PayloadOnlyPackage package, ServerFileHeader header, IOdinContext odinContext)
    {
        return await FileSystem.Storage.UpdatePayloads(
            // package.InternalFile,
            package.TempFile,
            targetFile: package.InternalFile,
            payloadsDescriptors: package.GetFinalPayloadDescriptors(),
            odinContext);
    }
}