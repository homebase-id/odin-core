using System;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// The <see cref="FileTtl"/> encoding: one long, three behaviours. Zero is the value every file
/// written before the field existed deserializes to, which is what lets the field ship without a
/// migration.
/// </summary>
[TestFixture]
public class FileTtlTests
{
    private static readonly UnixTimeUtc Created = new(1_700_000_000_000);

    // ---- backwards compatibility: the test that matters most --------------------------------

    [Test]
    public void FileStoredBeforeTheFieldExistedNeverExpires()
    {
        // hdrFileMetaData as written before Ttl existed. It has no such key, so it must read as 0,
        // and 0 must mean "never" - anything else silently starts deleting existing files.
        const string legacyJson = """
            {"ReferencedFile":null,"TransitCreated":0,"TransitUpdated":0,"IsEncrypted":true,
             "OriginalAuthor":null,"Payloads":[],"DataSource":null}
            """;

        var dto = OdinSystemSerializer.Deserialize<FileMetadataDto>(legacyJson);

        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Ttl, Is.EqualTo(FileTtl.Never));
        Assert.That(FileTtl.ExpiresAt(dto.Ttl, Created), Is.Null, "a file with no Ttl must never expire");
        Assert.That(FileTtl.HasExpired(dto.Ttl, UnixTimeUtc.Now()), Is.False);
    }

    [Test]
    public void TtlRoundTripsThroughTheDto()
    {
        var metadata = new FileMetadata { Ttl = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20)) };

        var json = OdinSystemSerializer.Serialize(new FileMetadataDto(metadata));
        var dto = OdinSystemSerializer.Deserialize<FileMetadataDto>(json);

        Assert.That(dto!.Ttl, Is.EqualTo(-1_200_000));
    }

    // ---- the encoding -----------------------------------------------------------------------

    [Test]
    public void AfterProducesAnAbsoluteTimeInTheFuture()
    {
        var ttl = FileTtl.After(TimeSpan.FromDays(90));

        Assert.That(FileTtl.IsAbsolute(ttl));
        Assert.That(FileTtl.IsPendingFirstRead(ttl), Is.False);
        Assert.That(ttl, Is.GreaterThan(UnixTimeUtc.Now().milliseconds));
    }

    [Test]
    public void AfterFirstReadIsANegativeDurationInMilliseconds()
    {
        Assert.That(FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20)), Is.EqualTo(-1_200_000));
        Assert.That(FileTtl.IsPendingFirstRead(FileTtl.AfterFirstRead(TimeSpan.FromSeconds(10))));
    }

    [Test]
    public void ResolvingOnFirstReadIsNowMinusTtl()
    {
        var now = new UnixTimeUtc(5_000);
        var ttl = FileTtl.AfterFirstRead(TimeSpan.FromSeconds(10)); // -10_000

        // now() - Ttl, and Ttl is negative, so this is now() + 10s
        Assert.That(FileTtl.ResolveFirstRead(ttl, now), Is.EqualTo(15_000));
    }

    [Test]
    public void OnlyANegativeTtlCanBeResolved()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FileTtl.ResolveFirstRead(FileTtl.Never, UnixTimeUtc.Now()));
        Assert.Throws<ArgumentOutOfRangeException>(() => FileTtl.ResolveFirstRead(12345, UnixTimeUtc.Now()));
    }

    [Test]
    public void AnAbsoluteTtlExpiresAtItsOwnValue()
    {
        var ttl = FileTtl.At(new UnixTimeUtc(10_000));

        Assert.That(FileTtl.ExpiresAt(ttl, Created), Is.EqualTo(10_000));
        Assert.That(FileTtl.HasExpired(ttl, new UnixTimeUtc(9_999)), Is.False);
        Assert.That(FileTtl.HasExpired(ttl, new UnixTimeUtc(10_000)), Is.True);
    }

    [Test]
    public void AnUnreadPendingTtlStillDiesAtItsBackstop()
    {
        // A message nobody ever opens must not live forever.
        var ttl = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20));

        var dueAt = FileTtl.ExpiresAt(ttl, Created);

        Assert.That(dueAt, Is.EqualTo(Created.milliseconds + (long)FileTtl.UnreadBackstop.TotalMilliseconds));
    }

    // ---- shorten-only -----------------------------------------------------------------------

    [Test]
    public void AnUpdateMayBringDeathForward()
    {
        var existing = FileTtl.At(new UnixTimeUtc(10_000));
        var sooner = FileTtl.At(new UnixTimeUtc(5_000));

        Assert.That(FileTtl.Extends(sooner, existing, Created), Is.False);
        Assert.That(FileTtl.Shortest(sooner, existing, Created), Is.EqualTo(sooner));
    }

    [Test]
    public void AnUpdateMayNotPushDeathOut()
    {
        var existing = FileTtl.At(new UnixTimeUtc(5_000));
        var later = FileTtl.At(new UnixTimeUtc(10_000));

        Assert.That(FileTtl.Extends(later, existing, Created), Is.True);
        Assert.That(FileTtl.Shortest(later, existing, Created), Is.EqualTo(existing));
    }

    [Test]
    public void AnUpdateMayNotClearAnExistingTtl()
    {
        var existing = FileTtl.At(new UnixTimeUtc(5_000));

        Assert.That(FileTtl.Extends(FileTtl.Never, existing, Created), Is.True);
        Assert.That(FileTtl.Shortest(FileTtl.Never, existing, Created), Is.EqualTo(existing));
    }

    [Test]
    public void AFileThatNeverExpiredMayBeGivenATtl()
    {
        var candidate = FileTtl.At(new UnixTimeUtc(5_000));

        Assert.That(FileTtl.Extends(candidate, FileTtl.Never, Created), Is.False);
        Assert.That(FileTtl.Shortest(candidate, FileTtl.Never, Created), Is.EqualTo(candidate));
    }

    [Test]
    public void ResolvedTtlDoesNotCountAsExtendingItsOwnPendingForm()
    {
        // The sender still holds the original duration while the recipient's copy has already
        // resolved on their first read. Clamping must keep the resolved (sooner) time rather than
        // reverting to the backstop, and must not throw - see FileTtl.Shortest.
        var pending = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20));
        var resolved = FileTtl.At(new UnixTimeUtc(Created.milliseconds + 1000));

        Assert.That(FileTtl.Extends(pending, resolved, Created), Is.True);
        Assert.That(FileTtl.Shortest(pending, resolved, Created), Is.EqualTo(resolved));
    }
}
