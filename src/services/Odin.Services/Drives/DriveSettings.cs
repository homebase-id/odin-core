#nullable enable
using System.Collections.Generic;
using Odin.Core.Exceptions;

namespace Odin.Services.Drives;

/// <summary>The settings a drive may have under its sharing model (docs/terminology.md, "Sharing models").</summary>
public static class DriveSettings
{
    /// <summary>
    /// A drive's settings, under its sharing model, whenever one of them changes: at creation, conversion and every
    /// setter.  The server can't open what a zero-knowledge drive holds, so nothing reads it on the server's behalf
    /// (anonymous reads, the CDN), the feed never distributes from it (subscriptions), and it can't be marked as a
    /// community channel, whose posts the server re-locks and sends on.  A Private Share drive can't be <c>OwnerOnly</c>
    /// either, which keeps every file <c>Owner</c>: it could share nothing.
    /// </summary>
    public static void AssertFit(DriveSharingModel sharingModel, bool ownerOnly, bool allowAnonymousReads, bool allowCdn,
        bool allowSubscriptions, IReadOnlyDictionary<string, string>? attributes)
    {
        if (!sharingModel.IsZeroKnowledgeDrive())
        {
            return;
        }

        if (allowAnonymousReads || allowCdn || allowSubscriptions)
        {
            throw new OdinClientException($"A {sharingModel} drive allows neither anonymous reads, the CDN nor subscriptions",
                OdinClientErrorCode.ArgumentError);
        }

        if (BuiltInDriveAttributes.IsCollaborativeChannelIn(attributes))
        {
            throw new OdinClientException($"A {sharingModel} drive can't be marked as a community channel " +
                                          $"({BuiltInDriveAttributes.IsCollaborativeChannel}): the channel needs the server to hold its key",
                OdinClientErrorCode.ArgumentError);
        }

        if (sharingModel == DriveSharingModel.SelectiveShare && ownerOnly)
        {
            throw new OdinClientException("A SelectiveShare drive can't be owner only: none of its files could be shared",
                OdinClientErrorCode.ArgumentError);
        }
    }

    /// <summary>
    /// Brings <paramref name="drive"/>'s settings into line with <paramref name="sharingModel"/>, as a conversion does:
    /// anonymous reads, the CDN and subscriptions are turned off, since nothing serves or sends a zero-knowledge drive's
    /// files on the server's behalf anyway, so no conversion (nor a re-key after recovery) fails on them.  The settings
    /// that say what the drive is for (owner only, the community channel) aren't changed: a drive that has one the model
    /// refuses isn't converted.  Whether anything was turned off.
    /// </summary>
    public static bool FitForConversion(StorageDriveData drive, DriveSharingModel sharingModel)
    {
        var turnedOff = drive.AllowAnonymousReads || drive.AllowCdn || drive.AllowSubscriptions;
        drive.AllowAnonymousReads = false;
        drive.AllowCdn = false;
        drive.AllowSubscriptions = false;
        AssertFit(sharingModel, drive.OwnerOnly, false, false, false, drive.Attributes);
        return turnedOff;
    }

    /// <summary>
    /// One setter's change under <paramref name="sharingModel"/>: only the setting it changes, as it will be, so a drive
    /// that has a setting its model refuses (set before this check) can still be brought into line, one setter at a time.
    /// Turning a setting off always fits.
    /// </summary>
    public static void AssertChangeFits(DriveSharingModel sharingModel, bool allowAnonymousReads = false, bool allowCdn = false,
        bool allowSubscriptions = false, IReadOnlyDictionary<string, string>? attributes = null) =>
        AssertFit(sharingModel, ownerOnly: false, allowAnonymousReads, allowCdn, allowSubscriptions, attributes);
}
