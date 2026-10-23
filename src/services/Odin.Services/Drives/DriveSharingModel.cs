namespace Odin.Services.Drives;

/// <summary>
/// Who holds which key to a drive (docs/zero-knowledge-drives.md, "Three sharing models").
/// </summary>
/// <remarks>
/// The label alone changes nothing: a drive is zero knowledge only once the browser has converted it
/// (<see cref="StorageDrive.IsZeroKnowledge"/>).  Stored as an int in the Drives table; the values are
/// frozen in the Drives migration that added the column.
/// </remarks>
public enum DriveSharingModel
{
    /// <summary>The drive key is shared with the members.  Today's behaviour, and the default.</summary>
    Collaborative = 0,

    /// <summary>Only the owner's devices and apps hold the drive key; others may only deposit.</summary>
    Private = 1,

    /// <summary>Only the owner holds the drive key; each share has its own key.</summary>
    SelectiveShare = 2,

    /// <summary>Not decided yet, e.g. the transient temp drive.</summary>
    Unclassified = 3
}
