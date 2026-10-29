using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._V2.ApiClient;

public static class OwnerDriveV2Extensions
{
    /// <summary>
    /// Hard-deletes one file as the owner through the V2 batch endpoint, asserting it went through
    /// </summary>
    public static async Task HardDeleteOneV2Async(this OwnerApiClientRedux owner, TargetDrive drive, Guid fileId, Guid versionTag)
    {
        var callerContext = new OwnerTestCase(drive);
        await callerContext.Initialize(owner);
        var writer = new DriveWriterV2Client(owner.Identity.OdinId, callerContext.GetFactory());
        var response = await writer.HardDeleteBatch(drive.Alias, new HardDeleteBatchRequestV2
        {
            Files = [new FileVersionTagV2 { FileId = fileId, VersionTag = versionTag }]
        });
        Assert.That((response.IsSuccessStatusCode, response.Content?.Results.Single().ErrorCode),
            Is.EqualTo((true, (OdinClientErrorCode?)null)), response.Error?.Content);
    }
}
