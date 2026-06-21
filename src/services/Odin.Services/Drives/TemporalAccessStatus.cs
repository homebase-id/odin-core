#nullable enable

using Odin.Core.Time;

namespace Odin.Services.Drives;

/// <summary>
/// Result of a temporal-read access verification ("can I use the temporal API on this drive, and is it working?").
/// Returned by the temporal <c>verify</c> endpoint so a caller can render a live access indicator (e.g. a green
/// check) without reading any data or generating an access notification.
/// </summary>
public class TemporalAccessStatus
{
    /// <summary>
    /// True when the caller currently holds temporal (or full) read access to the drive and the drive exists.
    /// </summary>
    public bool HasAccess { get; init; }

    /// <summary>
    /// The drive that was checked.
    /// </summary>
    public TargetDrive? TargetDrive { get; init; }

    /// <summary>
    /// The effective lookback window (seconds) the caller would be limited to, or null when the caller has
    /// unconstrained read access (no time clamp). Only meaningful when <see cref="HasAccess"/> is true.
    /// </summary>
    public long? WindowSeconds { get; init; }

    /// <summary>
    /// Server-set modified timestamp of the newest file on the drive, or 0 when the drive has no files (or the
    /// caller has no access). Lets a caller see the last data update — e.g. for a location drive, a parent can
    /// tell that updates have stopped (location turned off) without reading any content. Not clamped to
    /// <see cref="WindowSeconds"/>.
    /// </summary>
    public UnixTimeUtc NewestFileModified { get; init; }
}
