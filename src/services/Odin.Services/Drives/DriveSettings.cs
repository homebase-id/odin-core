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

    /// <summary>Every setting of <paramref name="drive"/> under <paramref name="sharingModel"/>, as a conversion asks.</summary>
    public static void AssertFit(StorageDriveData drive, DriveSharingModel sharingModel) =>
        AssertFit(sharingModel, drive.OwnerOnly, drive.AllowAnonymousReads, drive.AllowCdn, drive.AllowSubscriptions, drive.Attributes);

    /// <summary>
    /// One setter's change under <paramref name="sharingModel"/>: only the setting it changes, as it will be, so a drive
    /// that has a setting its model refuses (set before this check) can still be brought into line, one setter at a time.
    /// Turning a setting off always fits.
    /// </summary>
    public static void AssertChangeFits(DriveSharingModel sharingModel, bool allowAnonymousReads = false, bool allowCdn = false,
        bool allowSubscriptions = false, IReadOnlyDictionary<string, string>? attributes = null) =>
        AssertFit(sharingModel, ownerOnly: false, allowAnonymousReads, allowCdn, allowSubscriptions, attributes);
}
