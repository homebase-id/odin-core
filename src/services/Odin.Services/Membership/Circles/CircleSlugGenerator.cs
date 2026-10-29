#nullable enable
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;

namespace Odin.Services.Membership.Circles;

/// <summary>
/// Makes a circle's slug from its name when the caller does not name one (#1939).
/// </summary>
/// <remarks>
/// A circle's slug is its address within its owning app: <c>UNIQUE(identityId, AppId, CircleSlug)</c>,
/// <see cref="OdinSlug"/> rules, immutable once written.  Unlike drives and apps, a circle may be created
/// without one -- an app minting one circle per channel has nothing to name it by -- so the server makes
/// one, the same way the <c>Circle</c> table migration did for every existing circle.
/// <para>
/// A made slug never takes a built-in circle's slug, in any app.  The upgrades move built-in circles into
/// their apps after the fact, and a user circle named "Chat" must not be holding <c>chat</c> when they do.
/// A slug the caller names explicitly is not held to that: it is the caller's choice, in its own app.
/// </para>
/// </remarks>
public static class CircleSlugGenerator
{
    private const string FallbackSlug = "circle";
    private const int MaxSuffix = 9999;

    /// <summary>The circles whose slug is fixed in code: the built-in apps'.</summary>
    private static readonly List<CircleDefinition> FixedCircles = BuiltinApps.AllCircles.ToList();

    private static readonly HashSet<OdinSlug> BuiltinSlugs = FixedCircles.Select(c => c.Slug).ToHashSet();

    /// <summary>
    /// The slugs the platform's own circles carry: the built-in apps'.
    /// </summary>
    public static IEnumerable<string> BuiltinCircleSlugs() => FixedCircles.Select(c => c.Slug.Value);

    /// <summary>
    /// The fixed slug of a built-in circle, or null for any other circle.
    /// </summary>
    public static string? FixedSlugFor(System.Guid circleId) =>
        FixedCircles.FirstOrDefault(c => c.Id.Value == circleId) is { } circle ? circle.Slug.Value : null;

    /// <summary>
    /// A slug made from <paramref name="name"/> that is not in <paramref name="taken"/>, the slugs the
    /// owning app already holds.  The name's own slug if free, else <c>-2</c>, <c>-3</c>...; a name with
    /// nothing usable in it starts from <c>circle</c>.
    /// </summary>
    public static OdinSlug Generate(string? name, ISet<OdinSlug> taken)
    {
        bool IsFree(string candidate, out OdinSlug slug) =>
            OdinSlug.TryParse(candidate, out slug) && !taken.Contains(slug) && !BuiltinSlugs.Contains(slug);

        var baseSlug = AppSlugGenerator.Slugify(name) ?? FallbackSlug;
        if (IsFree(baseSlug, out var free))
        {
            return free;
        }

        for (var suffix = 2; suffix <= MaxSuffix; suffix++)
        {
            var tail = "-" + suffix;
            var trimmed = baseSlug[..System.Math.Min(baseSlug.Length, OdinSlug.MaxLength - tail.Length)].TrimEnd('-');
            var candidate = (trimmed.Length > 0 ? trimmed : FallbackSlug) + tail;
            if (IsFree(candidate, out free))
            {
                return free;
            }
        }

        throw new OdinSystemException($"Could not make a unique slug for circle '{name}'");
    }
}
