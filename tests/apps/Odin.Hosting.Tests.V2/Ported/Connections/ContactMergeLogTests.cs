using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Contacts;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Pure-logic tests for <see cref="ContactMergeLog"/>: which field changes count as an overwrite, and
/// how the append-only log is built and bounded. No host required.
/// </summary>
[TestFixture]
public class ContactMergeLogTests
{
    [Test]
    public void ComputeOverwrites_LogsReplacedNonEmptyField()
    {
        var existing = new ContactContent { Name = new ContactName { DisplayName = "Sam" } };
        var incoming = new ContactContent { Name = new ContactName { DisplayName = "Samwise" } };

        var changes = ContactMergeLog.ComputeOverwrites(existing, incoming);

        Assert.That(changes, Has.Count.EqualTo(1));
        Assert.That(changes["name.displayName"], Is.EqualTo("Sam"), "old value is recorded");
    }

    [Test]
    public void ComputeOverwrites_IgnoresFirstTimeFill()
    {
        var existing = new ContactContent { Name = new ContactName { DisplayName = "Sam" } };
        var incoming = new ContactContent { Email = new ContactEmail { Email = "sam@shire.example" } };

        var changes = ContactMergeLog.ComputeOverwrites(existing, incoming);

        Assert.That(changes, Is.Empty, "filling a previously-empty field is not an overwrite");
    }

    [Test]
    public void ComputeOverwrites_IgnoresUnchangedValue()
    {
        var existing = new ContactContent { Email = new ContactEmail { Email = "sam@shire.example" } };
        var incoming = new ContactContent { Email = new ContactEmail { Email = "sam@shire.example" } };

        var changes = ContactMergeLog.ComputeOverwrites(existing, incoming);

        Assert.That(changes, Is.Empty, "writing the same value is a no-op");
    }

    [Test]
    public void ComputeOverwrites_IgnoresFieldIncomingLeavesNull()
    {
        var existing = new ContactContent
        {
            Name = new ContactName { DisplayName = "Sam" },
            Email = new ContactEmail { Email = "sam@shire.example" }
        };
        // Incoming carries only a (changed) email; Name is null → the existing name is preserved, not overwritten.
        var incoming = new ContactContent { Email = new ContactEmail { Email = "frodo@shire.example" } };

        var changes = ContactMergeLog.ComputeOverwrites(existing, incoming);

        Assert.That(changes.Keys, Is.EquivalentTo(new[] { "email.email" }));
        Assert.That(changes, Does.Not.ContainKey("name.displayName"));
    }

    [Test]
    public void ComputeOverwrites_HandlesMultipleFieldsAndPaths()
    {
        var existing = new ContactContent
        {
            OdinId = "sam.dotyou.cloud",
            Name = new ContactName { GivenName = "Sam", Surname = "Gamgee" },
            Location = new ContactLocation { City = "Hobbiton" },
            Phone = new ContactPhone { Number = "111" },
            Birthday = new ContactBirthday { Date = "TA 2980" }
        };
        var incoming = new ContactContent
        {
            Name = new ContactName { GivenName = "Samwise", Surname = "Gamgee" }, // surname unchanged
            Location = new ContactLocation { City = "Bag End" },
            Phone = new ContactPhone { Number = "222" },
            Birthday = new ContactBirthday { Date = "TA 2980" } // unchanged
        };

        var changes = ContactMergeLog.ComputeOverwrites(existing, incoming);

        Assert.That(changes.Keys, Is.EquivalentTo(new[] { "name.givenName", "location.city", "phone.number" }));
        Assert.That(changes["name.givenName"], Is.EqualTo("Sam"));
        Assert.That(changes["location.city"], Is.EqualTo("Hobbiton"));
        Assert.That(changes["phone.number"], Is.EqualTo("111"));
    }

    [Test]
    public void BuildUpdatedLog_AppendsEntry_WithSourceAndTimestamp()
    {
        var changes = new Dictionary<string, string> { ["name.displayName"] = "Sam" };

        var bytes = ContactMergeLog.BuildUpdatedLog(null, changes, ContactMergeSource.Enrichment, new UnixTimeUtc(1234));
        var log = ContactMergeLog.Deserialize(bytes);

        Assert.That(log, Has.Count.EqualTo(1));
        Assert.That(log[0].By, Is.EqualTo("enrichment"));
        Assert.That(log[0].At, Is.EqualTo(1234));
        Assert.That(log[0].Changes["name.displayName"], Is.EqualTo("Sam"));
    }

    [Test]
    public void BuildUpdatedLog_AppendsToExistingLog()
    {
        var existing = new List<ContactMergeLogEntry>
        {
            new() { At = 1, By = "api", Changes = new Dictionary<string, string> { ["phone.number"] = "111" } }
        };

        var bytes = ContactMergeLog.BuildUpdatedLog(existing,
            new Dictionary<string, string> { ["name.displayName"] = "Sam" }, ContactMergeSource.Api, new UnixTimeUtc(2));
        var log = ContactMergeLog.Deserialize(bytes);

        Assert.That(log, Has.Count.EqualTo(2));
        Assert.That(log[0].Changes["phone.number"], Is.EqualTo("111"), "older entry kept, in order");
        Assert.That(log[1].Changes["name.displayName"], Is.EqualTo("Sam"));
    }

    [Test]
    public void BuildUpdatedLog_BoundsGrowth_DroppingOldest()
    {
        var log = new List<ContactMergeLogEntry>();
        // Append well past the cap; each entry tagged with its sequence number in displayName.
        for (var i = 0; i < ContactMergeLog.MaxEntries + 25; i++)
        {
            var bytes = ContactMergeLog.BuildUpdatedLog(log,
                new Dictionary<string, string> { ["name.displayName"] = i.ToString() },
                ContactMergeSource.Api, new UnixTimeUtc(i));
            log = ContactMergeLog.Deserialize(bytes);
        }

        Assert.That(log, Has.Count.EqualTo(ContactMergeLog.MaxEntries), "log is bounded");
        // Oldest dropped: the first surviving entry is #25, the last is the most recent.
        Assert.That(log[0].Changes["name.displayName"], Is.EqualTo("25"));
        Assert.That(log[^1].Changes["name.displayName"], Is.EqualTo((ContactMergeLog.MaxEntries + 24).ToString()));
    }
}
