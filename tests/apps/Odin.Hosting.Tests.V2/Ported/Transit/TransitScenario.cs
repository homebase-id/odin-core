using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// The two reads the transit-routing fixtures share: find a file by its global transit id, and check
/// what the sender's transfer history says about one recipient.
/// </summary>
/// <remarks>
/// Both replace a V1 client method that has no <c>_Universal</c> twin.
/// <see cref="QueryByGlobalTransitIdAsync"/> stands in for
/// <c>DriveApiClient.QueryBatch(FileSystemType, FileQueryParamsV1)</c>, keeping its result options
/// (<c>MaxRecords = 10</c>, <c>IncludeMetadataHeader = true</c>) so a "there is exactly one" assertion
/// still means what it did — <c>UniversalDriveApiClient.QueryByGlobalTransitId</c> caps at one record,
/// which would turn that assertion into a tautology.
/// <para>
/// <see cref="AssertTransferStatusAsync"/> replaces <c>DriveApiClientRedux.WaitForTransferStatus</c>,
/// which polls the history endpoint for up to ten seconds while the outbox background service works.
/// That service is registered but never started here, so the caller drains the outbox first
/// (<c>Sync.DrainOutboxAsync</c>) and this then asserts the settled status once.
/// </para>
/// </remarks>
internal static class TransitScenario
{
    /// <summary>Everything on <paramref name="owner"/>'s drive carrying <paramref name="file"/>'s global transit id.</summary>
    public static async Task<List<SharedSecretEncryptedFileHeader>> QueryByGlobalTransitIdAsync(
        OwnerSession owner,
        GlobalTransitIdFileIdentifier file,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var response = await owner.V1.Drive.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = file.TargetDrive,
                GlobalTransitId = [file.GlobalTransitId]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        }, fileSystemType);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return [.. response.Content!.SearchResults];
    }

    /// <summary>
    /// The sender's transfer history for <paramref name="file"/> reports
    /// <paramref name="expected"/> for <paramref name="recipient"/>. Drain the sender's outbox first.
    /// </summary>
    public static async Task AssertTransferStatusAsync(
        OwnerSession sender,
        ExternalFileIdentifier file,
        OdinId recipient,
        LatestTransferStatus expected,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var response = await sender.V1.Drive.GetTransferHistory(file, fileSystemType);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var item = response.Content!.GetHistoryItem(recipient);
        Assert.That(item, Is.Not.Null, $"no transfer-history item for {recipient}");
        Assert.That(item!.LatestTransferStatus, Is.EqualTo(expected));
    }
}
