using System;
using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Registry;

namespace Odin.Services.Drives.FileSystem.Comment.Attachments;

/// <summary />
public class CommentPayloadStreamWriter : PayloadStreamWriterBase
{
    /// <summary />
    public CommentPayloadStreamWriter(
        CommentFileSystem fileSystem,
        TenantQuotaGuard quotaGuard)
        : base(fileSystem, quotaGuard)
    {
    }

    protected override Task ValidatePayloads(PayloadOnlyPackage package, ServerFileHeader header)
    {
        return Task.CompletedTask;
    }

    protected override async Task<Guid> UpdatePayloads(PayloadOnlyPackage package, ServerFileHeader header, IOdinContext odinContext)
    {
        return await FileSystem.Storage.UpdatePayloads(
            package.UploadFile,
            targetFile: package.InternalFile,
            incomingPayloads: package.GetFinalPayloadDescriptors(),
            odinContext,
            package.InstructionSet.VersionTag.GetValueOrDefault());
    }
}