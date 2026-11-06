#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Refit;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Whom the owner shares with (file-access.md, "Who a file is shared with"): the query batch's <c>SharedWith</c> finds the
/// files with a live share for a circle, a page at a time, for whoever manages the drive's shares.
/// </summary>
public partial class SelectiveShareTests
{
    [Test]
    public async Task TheOwnerFindsTheFilesSharedWithACirclePageByPage()
    {
        var s = await ArrangeAsync();
        var other = await s.CircleWithKeyAsync(DrivePermission.Read);
        var expiresAt = UnixTimeUtc.Now().AddSeconds(3);
        var expired = await s.UploadAsync(SecurityGroupType.Owner);
        await s.ShareAsync(expired, DrivePermission.Read, expiresAt);
        var first = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)]);
        var second = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write)]);
        var theOthers = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read, circleId: other)]);
        var withdrawn = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)]);
        Assert.That((await s.Shares.WithdrawShare(s.Drive.Alias, withdrawn, s.CircleId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await s.UploadAsync(SecurityGroupType.Owner);
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, expiresAt.milliseconds - UnixTimeUtc.Now().milliseconds) + 200));

        foreach (var (caller, who) in new (IV2Caller, string)[] { (s.Frodo, "the owner console"), (s.App, "the drive's app") })
        {
            Assert.That(await SharedWithAsync(caller, s, [s.CircleId]), Is.EquivalentTo(new[] { first, second }),
                $"{who}: live shares only, not the withdrawn, expired or other circle's");
            Assert.That(await SharedWithAsync(caller, s, [s.CircleId, other]), Is.EquivalentTo(new[] { first, second, theOthers }),
                $"{who}: either circle");
        }
    }

    [Test]
    public async Task OnlyWhoeverManagesTheSharesFindsWhomTheyAreWith()
    {
        var s = await ArrangeAsync();
        await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)]);

        var otherApp = await AppSession.SetupAsync(s.Frodo, s.Drive, DrivePermission.All);
        var byOtherApp = await otherApp.Drives.Reader.GetBatchAsync(s.Drive.Alias, SharedWithQuery(s.Drive, [s.CircleId]));
        Assert.That(byOtherApp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"another app of the owner's: {byOtherApp.Error?.Content}");

        var bySam = await s.Sam.Drives.Peer.QueryBatchAsync(s.Frodo.Identity, s.Drive.Alias, SharedWithQuery(s.Drive, [s.CircleId]));
        Assert.That(bySam.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"a member, over the Peer API: {bySam.Error?.Content}");

        var collaborative = TargetDrive.NewTargetDrive();
        await s.Frodo.Admin.CreateDrive(collaborative, "not shared this way", allowAnonymousReads: false);
        DriveAsserts.AssertRejected(await s.Frodo.Drives.Reader.GetBatchAsync(collaborative.Alias, SharedWithQuery(collaborative, [s.CircleId])),
            OdinClientErrorCode.ArgumentError);
    }

    // Every file shared with any of the circles, one per page
    private static async Task<List<Guid>> SharedWithAsync(IV2Caller caller, Arranged s, List<Guid> circleIds)
    {
        var found = new List<Guid>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var request = SharedWithQuery(s.Drive, circleIds);
            request.ResultOptionsRequest.CursorState = cursor;
            var query = await caller.Drives.Reader.GetBatchAsync(s.Drive.Alias, request);
            Assert.That(query.IsSuccessStatusCode, Is.True, $"page {page} answered {query.StatusCode}: {query.Error?.Content}");
            var ids = query.Content!.SearchResults.Select(h => h.FileId).ToList();
            if (ids.Count == 0)
            {
                Assert.That(found, Is.Unique, "no file twice");
                return found;
            }

            Assert.That(ids, Has.Count.EqualTo(1), $"page {page}");
            found.AddRange(ids);
            cursor = query.Content.CursorState;
        }

        Assert.Fail($"still paging after 10 pages: {string.Join(", ", found)}");
        return found;
    }

    private static QueryBatchRequest SharedWithQuery(TargetDrive drive, List<Guid> circleIds) => new()
    {
        QueryParams = new FileQueryParamsV1 { TargetDrive = drive, SharedWith = circleIds },
        ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 1, IncludeMetadataHeader = true }
    };
}
