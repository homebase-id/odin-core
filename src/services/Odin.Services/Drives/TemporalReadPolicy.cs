#nullable enable

using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Drives;

/// <summary>
/// Resolves the effective <see cref="DrivePermission.ConditionalTemporalRead"/> cutoff for a request.
/// The effective window is <c>min(drive ceiling, circle window)</c>, each defaulting to
/// <see cref="TemporalRead.DefaultWindowSeconds"/>. Files whose server-set <c>modified</c> timestamp is
/// older than the returned cutoff must not be served by the temporal API.
/// </summary>
public static class TemporalReadPolicy
{
    /// <summary>
    /// Returns the modified-date cutoff (inclusive lower bound) to clamp a temporal read to, or null when
    /// no clamp applies — i.e. the caller holds an unconstrained <see cref="DrivePermission.Read"/> grant
    /// (full read access, including system/owner contexts).
    /// </summary>
    public static UnixTimeUtc? ResolveCutoff(IOdinContext odinContext, StorageDrive drive)
    {
        var window = ResolveWindowSeconds(odinContext, drive);
        return window == null ? null : UnixTimeUtc.Now().AddSeconds(-window.Value);
    }

    /// <summary>
    /// Returns the effective temporal read window (seconds), or null when the caller holds an unconstrained
    /// <see cref="DrivePermission.Read"/> grant (no clamp — full read access, including system/owner contexts).
    /// </summary>
    public static long? ResolveWindowSeconds(IOdinContext odinContext, StorageDrive drive)
    {
        var pc = odinContext.PermissionsContext;

        // A full read grant (or system/owner) is never clamped.
        if (pc.HasDrivePermission(drive.Id, DrivePermission.Read))
        {
            return null;
        }

        var ceiling = ParseCeilingSeconds(drive.Attributes);
        var circle = pc.GetTemporalCircleWindowSeconds(drive.Id);

        return ComputeEffectiveWindowSeconds(ceiling, circle);
    }

    /// <summary>
    /// The effective window is the smaller of the drive ceiling and the circle window, each defaulting
    /// to <see cref="TemporalRead.DefaultWindowSeconds"/> when unset. e.g. a 30-day drive ceiling with a
    /// 7-day circle window yields 7 days.
    /// </summary>
    internal static long ComputeEffectiveWindowSeconds(long? driveCeilingSeconds, long? circleWindowSeconds)
    {
        var ceiling = driveCeilingSeconds ?? TemporalRead.DefaultWindowSeconds;
        var circle = circleWindowSeconds ?? TemporalRead.DefaultWindowSeconds;
        return Math.Min(ceiling, circle);
    }

    /// <summary>
    /// Reads the drive-level ceiling (seconds) from the drive attributes, or null when unset / invalid /
    /// non-positive (treated as "use the default").
    /// </summary>
    internal static long? ParseCeilingSeconds(IDictionary<string, string>? attributes)
    {
        if (attributes != null &&
            attributes.TryGetValue(TemporalRead.MaxAgeAttributeKey, out var raw) &&
            long.TryParse(raw, out var seconds) &&
            seconds > 0)
        {
            return seconds;
        }

        return null;
    }
}
