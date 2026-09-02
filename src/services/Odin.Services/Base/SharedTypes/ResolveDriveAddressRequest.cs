namespace Odin.Services.Base.SharedTypes;

/// <summary>
/// Asks an identity what <c>/apps/{AppSlug}/drives/{DriveSlug}</c> names on *its* side
/// (docs/drive-addressing.md).  Slugs are unique per identity, so only the host holding the drive can
/// answer -- the same as a drive id, which is also only meaningful on the identity that issued it.
/// </summary>
public class ResolveDriveAddressRequest
{
    public string AppSlug { get; set; }
    public string DriveSlug { get; set; }
}
