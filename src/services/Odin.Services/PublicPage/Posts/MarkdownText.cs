#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Odin.Services.PublicPage.Posts;

/// <summary>Plain text of a Markdown post caption, for titles, meta tags, JSON-LD, RSS and the page heading</summary>
public static class MarkdownText
{
    private static readonly Regex Rule = new(@"^\s*([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex Prefix = new(@"^\s*(?:#{1,6}\s+|>\s?|[-*+]\s+|\d{1,3}[.)]\s+)+", RegexOptions.Compiled);

    private static readonly (Regex Pattern, string Replacement)[] Inline =
    [
        (new Regex(@"!?\[([^\]\n]+)\]\([^)\s]+(?:\s+""[^""]*"")?\)", RegexOptions.Compiled), "$1"),
        (new Regex(@"`+([^`\n]+)`+", RegexOptions.Compiled), "$1"),
        (new Regex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*", RegexOptions.Compiled), "$1"),
        (new Regex(@"(?<!\w)__(?=\S)(.+?)(?<=\S)__(?!\w)", RegexOptions.Compiled), "$1"),
        (new Regex(@"~~(?=\S)(.+?)(?<=\S)~~", RegexOptions.Compiled), "$1"),
        (new Regex(@"\*(?=\S)([^*\n]+?)(?<=\S)\*", RegexOptions.Compiled), "$1"),
        (new Regex(@"(?<!\w)_(?=\S)([^_\n]+?)(?<=\S)_(?!\w)", RegexOptions.Compiled), "$1")
    ];

    /// <param name="markdown">The caption as written</param>
    /// <param name="singleLine">Join the lines with a space, for titles and meta tags</param>
    /// <returns>Non-empty lines joined with a newline, or a space when <paramref name="singleLine"/></returns>
    public static string ToPlain(string? markdown, bool singleLine = false)
    {
        var lines = new List<string>();
        var inFence = false;
        foreach (var raw in (markdown ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (!inFence)
            {
                if (Rule.IsMatch(line)) continue;
                line = Prefix.Replace(line, "");
                foreach (var (pattern, replacement) in Inline) line = pattern.Replace(line, replacement);
            }

            line = Regex.Replace(line, @"\s+", " ").Trim();
            if (line.Length > 0) lines.Add(line);
        }

        return string.Join(singleLine ? " " : "\n", lines);
    }
}
