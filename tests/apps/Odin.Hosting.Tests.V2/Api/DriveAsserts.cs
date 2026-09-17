using System;
using System.Net;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Core.Storage;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Assertions that recur across drive fixtures, in the same spirit as <see cref="Peer.PeerFlow"/>:
/// shared test vocabulary rather than a client facade.
/// </summary>
/// <remarks>
/// These exist because the ported fixtures kept re-inlining them — the query-by-datatype check
/// alone had seven copies across two files. Both V1 and V2 overloads are provided: a fixture whose
/// system under test is a V1 endpoint should seed and verify over V1 so the port keeps exercising
/// the path the original did.
/// </remarks>
public static class DriveAsserts
{
    /// <summary>
    /// The drive returns exactly one file for <paramref name="dataType"/>, and it is the expected
    /// one. V1 overload — queries through <see cref="UniversalDriveApiClient"/>.
    /// </summary>
    public static async Task AssertFileFoundByDataType(
        UniversalDriveApiClient client, TargetDrive targetDrive, int dataType, Guid expectedFileId)
    {
        var search = await client.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = targetDrive, DataType = [dataType] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(search.IsSuccessStatusCode, Is.True);
        var hit = search.Content!.SearchResults.SingleOrDefault();
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.FileId, Is.EqualTo(expectedFileId));
    }

    /// <summary>
    /// As the V1 overload, querying through the owner's V2 reader instead.
    /// </summary>
    public static async Task AssertFileFoundByDataType(
        OwnerSession owner, TargetDrive targetDrive, int dataType, Guid expectedFileId)
    {
        var search = await owner.Drives.Reader.GetBatchAsync(targetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = targetDrive, DataType = [dataType] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(search.IsSuccessStatusCode, Is.True);
        var hit = search.Content!.SearchResults.SingleOrDefault();
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.FileId, Is.EqualTo(expectedFileId));
    }

    /// <summary>
    /// One file's outcome in a <c>SendReadReceipt</c> response: the call returned
    /// <paramref name="expectedHttpStatus"/>, the response carries a record for
    /// <paramref name="file"/>, and that record's entry for <paramref name="expectedRecipient"/>
    /// reports <paramref name="expectedStatus"/>.
    /// </summary>
    /// <param name="expectedRecipient">
    /// The original sender the receipt is addressed to. Pass <c>null</c> for the self-receipt case,
    /// where the server answers with a single recipient-less row.
    /// </param>
    public static void AssertReadReceiptStatus(
        ApiResponse<SendReadReceiptResult> response,
        ExternalFileIdentifier file,
        OdinId? expectedRecipient,
        SendReadReceiptResultStatus expectedStatus,
        HttpStatusCode expectedHttpStatus = HttpStatusCode.OK)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedHttpStatus));
        var result = response.Content;
        Assert.That(result, Is.Not.Null);

        var item = result!.Results.SingleOrDefault(d => d.File == file);
        Assert.That(item, Is.Not.Null, "no record for file");

        if (expectedRecipient is { } recipient)
        {
            var statusItem = item!.Status.SingleOrDefault(i => i.Recipient == recipient);
            Assert.That(statusItem, Is.Not.Null);
            Assert.That(statusItem!.Status, Is.EqualTo(expectedStatus));
        }
        else
        {
            var statusItem = item!.Status.Single();
            Assert.That(statusItem.Recipient, Is.Null);
            Assert.That(statusItem.Status, Is.EqualTo(expectedStatus));
        }
    }

    /// <summary>
    /// The sender's transfer history for <paramref name="file"/> reports <paramref name="expected"/>
    /// for <paramref name="recipient"/>. Drain the sender's outbox first.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in a Ported folder because nine fixtures across Peer, Transit and
    /// Connections assert this same shape; only three of them shared a copy, and only because it
    /// happened to sit in a folder they already imported.
    /// </remarks>
    public static async Task AssertTransferStatus(
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
