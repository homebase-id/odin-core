namespace Odin.Services.Drives;

/// <summary>
/// Constants for the <see cref="DrivePermission.ConditionalTemporalRead"/> feature: a time-boxed read
/// grant whose lookback window is configured per-drive (an <see cref="StorageDrive.Attributes"/> value)
/// and optionally narrowed per circle grant. The effective window is the smaller of the two; either
/// unset falls back to <see cref="DefaultWindowSeconds"/>.
/// </summary>
public static class TemporalRead
{
    /// <summary>
    /// Drive attribute key holding the drive-level ceiling for the temporal read window, in seconds.
    /// </summary>
    public const string MaxAgeAttributeKey = "temporalReadMaxAgeSeconds";

    /// <summary>
    /// Default temporal read window (1 week) used when neither the drive ceiling nor the circle
    /// override is set, or when a configured value cannot be parsed.
    /// </summary>
    public const long DefaultWindowSeconds = 60 * 60 * 24 * 7;
}
