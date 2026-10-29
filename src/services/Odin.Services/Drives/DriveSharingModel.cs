namespace Odin.Services.Drives;

/// <summary>
/// Who holds which key to a drive (docs/zero-knowledge-drives.md, "Three sharing models").
/// </summary>
/// <remarks>
/// Every drive starts Collaborative; conversion is what makes one Private or SelectiveShare, so the label never claims
/// more than <see cref="StorageDrive.IsZeroKnowledge"/>.  Stored as an int in the Drives table; the
/// values are frozen.
/// </remarks>
public enum DriveSharingModel
{
    /// <summary>The drive key is shared with the members.  Today's behaviour, and the default.</summary>
    Collaborative = 0,

    /// <summary>Only the owner's devices and apps hold the drive key; others may only deposit.</summary>
    Private = 1,

    /// <summary>
    /// Only the owner holds the drive key; each share has its own key.  Shares aren't built yet, so for now such a
    /// drive behaves as a Private one (Filesync).
    /// </summary>
    SelectiveShare = 2,

    /// <summary>Not decided yet.  Nothing sets it today.</summary>
    Unclassified = 3
}

public static class DriveSharingModelExtensions
{
    /// <summary>The models whose drives the server holds no key to: Private and SelectiveShare.</summary>
    public static bool IsZeroKnowledge(this DriveSharingModel model) =>
        model is DriveSharingModel.Private or DriveSharingModel.SelectiveShare;
}
