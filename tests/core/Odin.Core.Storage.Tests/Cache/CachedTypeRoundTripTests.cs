using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Odin.Core.Storage.Tests.Cache;

#nullable enable

//
// Round-trips every record type returned by a cached table or QueryBatch through the
// production FusionCache serializer. This is the contract the L2 (Redis) path has to
// honor: shape preservation, no silent default(...) corruption.
//
// We test the serializer directly rather than going through FusionCache + Redis
// because FusionCache 2.1's L1-bypass options (SkipMemoryCacheRead/Write) have
// known bugs (see https://github.com/ZiggyCreatures/FusionCache/issues/419), so we
// can't reliably force an L2 round-trip from a single FusionCache instance. Testing
// the serializer directly catches the same class of issues without depending on
// broken FusionCache machinery.
//
public class CachedTypeRoundTripTests
{
    [Test]
    public void QueryBatchCachedResult_RoundTrips()
    {
        var original = new QueryBatchCachedResult(
            Records: new List<DriveMainIndexRecord>
            {
                new() { driveId = Guid.NewGuid(), fileId = Guid.NewGuid() },
            },
            MoreRows: true,
            Cursor: new QueryBatchCursor());

        var copy = RoundTrip(original);

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy!.Records, Is.Not.Null);
        Assert.That(copy.Records.Count, Is.EqualTo(1));
        Assert.That(copy.MoreRows, Is.True);
        Assert.That(copy.Cursor, Is.Not.Null);
    }

    [Test]
    public void QueryModifiedCachedResult_RoundTrips()
    {
        var original = new QueryModifiedCachedResult(
            Records: new List<DriveMainIndexRecord>(),
            MoreRows: false,
            Cursor: "next");

        var copy = RoundTrip(original);

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy!.Records, Is.Not.Null);
        Assert.That(copy.MoreRows, Is.False);
        Assert.That(copy.Cursor, Is.EqualTo("next"));
    }

    [Test]
    public void ConnectionsPage_RoundTrips()
    {
        var original = new ConnectionsPage(
            Records: new List<ConnectionsRecord>
            {
                new() { identity = new OdinId("frodo.baggins.me"), displayName = "Frodo" },
            },
            NextCursor: "cursor-1");

        var copy = RoundTrip(original);

        Assert.That(copy!.Records.Count, Is.EqualTo(1));
        Assert.That(copy.NextCursor, Is.EqualTo("cursor-1"));
    }

    [Test]
    public void AppNotificationsPage_RoundTrips()
    {
        var original = new AppNotificationsPage(
            Records: new List<AppNotificationsRecord>(),
            Cursor: "abc");

        var copy = RoundTrip(original);

        Assert.That(copy!.Records, Is.Not.Null);
        Assert.That(copy.Cursor, Is.EqualTo("abc"));
    }

    [Test]
    public void CirclePage_RoundTrips()
    {
        var original = new CirclePage(
            Records: new List<CircleRecord>
            {
                new() { circleId = Guid.NewGuid(), circleName = "ring-bearers" },
            },
            NextCursor: Guid.NewGuid());

        var copy = RoundTrip(original);

        Assert.That(copy!.Records.Count, Is.EqualTo(1));
        Assert.That(copy.NextCursor, Is.Not.Null);
    }

    [Test]
    public void FollowsMePage_RoundTrips()
    {
        var original = new FollowsMePage(
            Followers: new List<string> { "a", "b" },
            NextCursor: "next");

        var copy = RoundTrip(original);

        Assert.That(copy!.Followers, Is.EquivalentTo(new[] { "a", "b" }));
        Assert.That(copy.NextCursor, Is.EqualTo("next"));
    }

    [Test]
    public void ImFollowingPage_RoundTrips()
    {
        var original = new ImFollowingPage(
            Followers: new List<string> { "x" },
            NextCursor: "next");

        var copy = RoundTrip(original);

        Assert.That(copy!.Followers, Is.EquivalentTo(new[] { "x" }));
        Assert.That(copy.NextCursor, Is.EqualTo("next"));
    }

    [Test]
    public void DrivesPage_RoundTrips()
    {
        var original = new DrivesPage(
            Records: new List<DrivesRecord>(),
            NextCursor: UnixTimeUtc.Now(),
            NextRowId: 42L);

        var copy = RoundTrip(original);

        Assert.That(copy!.Records, Is.Not.Null);
        Assert.That(copy.NextRowId, Is.EqualTo(42L));
        Assert.That(copy.NextCursor, Is.Not.Null);
    }

    [Test]
    public void DriveSize_RoundTrips()
    {
        var original = new DriveSize(Count: 7, Size: 12345L);

        var copy = RoundTrip(original);

        Assert.That(copy!.Count, Is.EqualTo(7));
        Assert.That(copy.Size, Is.EqualTo(12345L));
    }

    private static T? RoundTrip<T>(T value)
    {
        var serializer = new FusionCacheSystemTextJsonSerializer();
        var bytes = serializer.Serialize(value);
        return serializer.Deserialize<T>(bytes);
    }
}
