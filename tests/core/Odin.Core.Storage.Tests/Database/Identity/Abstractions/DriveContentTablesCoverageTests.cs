using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions;

/// <summary>
/// Emptying or deleting a drive clears <see cref="MainIndexMeta.DriveContentTables"/> by driveId. A new table keyed
/// by a drive has to land there, or on the list below with the reason it is handled elsewhere -- otherwise its rows
/// would silently outlive the drive.
/// </summary>
public class DriveContentTablesCoverageTests
{
    private static readonly Dictionary<string, string> HandledElsewhere = new()
    {
        ["Drives"] = "the drive record itself; deleted with the drive, kept when emptied",
        ["Inbox"] = "keyed by boxId, with its own cache: TableInboxCached.DeleteBoxAsync",
        ["FollowsMe"] = "followers of the drive; deleted with the drive, kept when emptied",
        ["ImFollowing"] = "driveId names a remote identity's drive, not one of ours",
    };

    [Test]
    public void EveryDriveKeyedTableIsCleared()
    {
        var driveKeyed = IdentityDatabase.ExportableRecordTypes
            .Where(t => t.Value.GetProperties().Any(p =>
                p.Name.Equals("driveId", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals("boxId", StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.Key);

        var unaccounted = driveKeyed
            .Where(t => !MainIndexMeta.DriveContentTables.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Where(t => !HandledElsewhere.ContainsKey(t));

        Assert.That(unaccounted, Is.Empty);
    }

}
