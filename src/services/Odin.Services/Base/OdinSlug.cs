#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Odin.Core.Exceptions;

namespace Odin.Services.Base;

/// <summary>
/// The slug format shared by app, drive and drive-type slugs.
/// </summary>
/// <remarks>
/// A slug is a URL path segment <b>and</b> a wire address other identities resolve against, so it has to
/// survive both with no encoding.  The rule is from <c>docs/drive-addressing.md</c>, <i>Slug format</i>:
/// lowercase letters, digits and internal hyphens only; nothing that needs percent-encoding; nothing
/// readable as a path separator or as <c>.</c> / <c>..</c>.
/// <para>
/// <b>Validate and reject; never coerce.</b>  The value is immutable once written and ends up in other
/// identities' URLs, so silently lowercasing or stripping a character produces an address the caller did
/// not ask for.
/// </para>
/// <para>
/// <b>Matching is case-insensitive.</b>  Every stored slug is lowercase, so a caller's value is lowercased
/// to look it up (<see cref="ToLookupKey"/>) and compared with <see cref="Matches"/>.  That only finds what
/// is already there; it never names anything.
/// </para>
/// </remarks>
public static class OdinSlug
{
    public const int MaxLength = 14;

    private static readonly Regex Pattern = new("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);

    /// <summary>
    /// Segments a slug must never collide with, because a literal sibling at the same position would
    /// win the route.
    /// </summary>
    /// <remarks>
    /// Empty today: rooting the slug tree at <c>/apps</c> means <c>{appSlug}</c> sits under <c>/apps</c>
    /// and <c>{driveSlug}</c> under <c>/apps/{appSlug}/drives</c>, and neither position has a literal
    /// sibling.  Kept as a list anyway, and it must grow whenever a literal segment is added at either
    /// position -- that is the whole reason the check exists rather than being assumed away.
    /// </remarks>
    private static readonly HashSet<string> Reserved = [];

    public static bool IsValid(string? slug)
    {
        return !string.IsNullOrEmpty(slug)
               && slug.Length <= MaxLength
               && Pattern.IsMatch(slug)
               && !Reserved.Contains(slug);
    }

    /// <summary>The form a caller's slug is looked up by: stored slugs are lowercase.</summary>
    public static string ToLookupKey(string slug) => slug.ToLowerInvariant();

    /// <summary>Whether two slugs name the same thing, ignoring case.</summary>
    public static bool Matches(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Throws a client error unless the slug is present and well formed.  Null, empty and whitespace-only
    /// are all "missing": the server never names anything on the caller's behalf (#1901).
    /// </summary>
    public static void AssertRequired(string? slug, string name)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new OdinClientException($"{name} is required", OdinClientErrorCode.ArgumentError);
        }

        AssertValidOrNull(slug, name);
    }

    /// <summary>
    /// Throws if the slug is present and malformed.  Null or empty passes.
    /// </summary>
    public static void AssertValidOrNull(string? slug, string name)
    {
        if (string.IsNullOrEmpty(slug))
        {
            return;
        }

        if (!IsValid(slug))
        {
            throw new OdinClientException(
                $"{name} '{slug}' is not a valid slug: lowercase letters, digits and internal hyphens " +
                $"only, 1-{MaxLength} characters.",
                OdinClientErrorCode.ArgumentError);
        }
    }
}
