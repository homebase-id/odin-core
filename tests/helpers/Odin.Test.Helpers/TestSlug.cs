using Odin.Core;

namespace Odin.Test.Helpers;

/// <summary>
/// Drive slugs for test fixtures.  The server requires every drive to name its slug and type slug
/// (#1901); a helper whose test does not care what the drive is called names one with this instead.
/// </summary>
public static class TestSlug
{
    /// <summary>The type slug for a test drive that is not testing types.</summary>
    public static readonly OdinSlug DriveType = new("drive");

    /// <summary>A unique, valid drive slug: "t" plus 13 hex digits, inside the 14-character limit.</summary>
    public static OdinSlug New() => new("t" + Guid.NewGuid().ToString("N")[..13]);
}
