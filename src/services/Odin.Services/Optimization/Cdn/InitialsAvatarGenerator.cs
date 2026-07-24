#nullable enable

using System;
using System.Globalization;
using System.Text;

namespace Odin.Services.Optimization.Cdn;

/// <summary>
/// Generates a simple initials-based SVG avatar (e.g. "JB") used as a personalized fallback for
/// <c>/pub/image</c> when an identity has an Anonymous-tier Name attribute but no published photo.
/// </summary>
public static class InitialsAvatarGenerator
{
    // Curated palette (not raw random RGB) so every generated avatar has decent contrast against
    // the white initials text.
    private static readonly string[] Palette =
    [
        "#F87171", "#FB923C", "#FBBF24", "#A3E635", "#34D399", "#2DD4BF",
        "#22D3EE", "#60A5FA", "#818CF8", "#A78BFA", "#E879F9", "#FB7185"
    ];

    /// <summary>
    /// Attempts to build an initials avatar from <paramref name="givenName"/>/<paramref name="surname"/>.
    /// Returns false (and a null <paramref name="svgBase64"/>) when there's no given name to work with --
    /// callers should fall through to their own default/generic fallback in that case.
    /// </summary>
    public static bool TryGenerate(string? givenName, string? surname, string colorSeed, out string? svgBase64)
    {
        var initials = ExtractInitials(givenName, surname);
        if (initials == null)
        {
            svgBase64 = null;
            return false;
        }

        var color = Palette[(int)(StableHash(colorSeed) % (uint)Palette.Length)];
        var svg = BuildSvg(initials, color);
        svgBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        return true;
    }

    private static string? ExtractInitials(string? givenName, string? surname)
    {
        var first = FirstLetter(givenName);
        if (first == null)
        {
            return null;
        }

        var second = FirstLetter(surname);
        return second == null ? first : first + second;
    }

    // Iterates Unicode text elements (not raw chars) so surrogate-pair/astral characters aren't split,
    // and skips any leading non-letter elements (emoji, punctuation) rather than picking a garbage initial.
    private static string? FirstLetter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            if (element.Length > 0 && char.IsLetter(element, 0))
            {
                return element.ToUpperInvariant();
            }
        }

        return null;
    }

    // Deterministic across process restarts, unlike string.GetHashCode() (randomized per-process in
    // .NET) -- the same identity must always land on the same palette color.
    private static uint StableHash(string value)
    {
        const uint fnvOffsetBasis = 2166136261;
        const uint fnvPrime = 16777619;

        var hash = fnvOffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= fnvPrime;
        }

        return hash;
    }

    private static string XmlEscape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string BuildSvg(string initials, string colorHex)
    {
        var escapedInitials = XmlEscape(initials);
        var fontSize = initials.Length > 1 ? 90 : 110;

        return $"""
                <svg xmlns="http://www.w3.org/2000/svg" width="250" height="250" viewBox="0 0 250 250">
                  <rect width="250" height="250" fill="{colorHex}" />
                  <text x="125" y="125" dy=".35em" text-anchor="middle" font-family="sans-serif" font-size="{fontSize}" fill="#ffffff">{escapedInitials}</text>
                </svg>
                """;
    }
}
