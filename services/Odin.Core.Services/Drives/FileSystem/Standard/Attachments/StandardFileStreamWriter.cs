using System;
using System.Threading.Tasks;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload.Attachments;

namespace Odin.Core.Services.Drives.FileSystem.Standard.Attachments;

/// <summary />
public class StandardFilePayloadStreamWriter : PayloadStreamWriterBase
{
    /// <summary />
    public StandardFilePayloadStreamWriter(StandardFileSystem fileSystem, OdinContextAccessor contextAccessor)
        : base(fileSystem, contextAccessor)
    {
    }

    protected override Task ValidatePayloads(PayloadOnlyPackage package, ServerFileHeader header)
    {
        return Task.CompletedTask;
    }

    protected override async Task<Guid> UpdatePayloads(PayloadOnlyPackage package, ServerFileHeader header)
    {
        return await FileSystem.Storage.UpdatePayloads(
            // package.InternalFile,
            package.TempFile,
            targetFile: package.InternalFile,
            incomingPayloads: package.GetFinalPayloadDescriptors());
    }
}