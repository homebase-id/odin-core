#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Odin.Services.PublicPage.Posts;

/// <summary>Plain text of a Markdown post caption, for titles, meta tags, JSON-LD, RSS and the page heading</summary>
public static class MarkdownText
{
    private const int MaxTitleLength = 500;
    private const int MaxLength = 20_000;

    private static Regex R(string pattern) =>
        new(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private static readonly Regex Rule = R(@"^\s*([-*_])(\s*\1){2,}\s*$");
    private static readonly Regex Fence = R(@"^\s*(?:```|~~~)[^`]*$");
    private static readonly Regex Whitespace = R(@"\s+");
    private static readonly Regex Prefix = R(@"^\s*(?:#{1,6}\s+|>(?:\s+|$)|[-*+]\s+)+");
    private static readonly Regex ListPrefix = R(@"^\s*(?:#{1,6}\s+|>(?:\s+|$)|[-*+]\s+|\d{1,3}[.)]\s+)+");

    private static readonly (Regex Pattern, string Replacement)[] Inline =
    [
        (R(@"!?\[([^\]\n]+)\]\((?:[^()\s]|\([^()\s]*\))+(?:\s+""[^""]*"")?\)"), "$1"),
        (R(@"`+([^`\n]+)`+"), "$1"),
        (R(@"(\*\*|~~)(?=\S)(.+?)(?<=\S)\1"), "$2"),
        (R(@"(?<!\w)__(?=\S)(.+?)(?<=\S)__(?!\w)"), "$1"),
        (R(@"(?<!\w)\*(?=\S)([^*\n]+?)(?<=\S)\*(?!\w)"), "$1"),
        (R(@"(?<!\w)_(?=\S)([^_\n]+?)(?<=\S)_(?!\w)"), "$1")
    ];

    /// <summary>One line, for titles and meta tags: lines joined with a space, numbers like "100. Things" kept</summary>
    public static string ToTitle(string? markdown) => Convert(markdown, true);

    /// <summary>Non-empty lines joined with a newline</summary>
    public static string ToPlain(string? markdown) => Convert(markdown, false);

    private static string Convert(string? markdown, bool singleLine)
    {
        var text = markdown ?? "";
        var max = singleLine ? MaxTitleLength : MaxLength;
        if (text.Length > max) text = text[..max];

        try
        {
            var prefix = singleLine ? Prefix : ListPrefix;
            var lines = new List<string>();
            var inFence = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (Fence.IsMatch(line))
                {
                    inFence = !inFence;
                    continue;
                }

                if (!inFence)
                {
                    if (Rule.IsMatch(line)) continue;
                    line = prefix.Replace(line, "");
                    foreach (var (pattern, replacement) in Inline) line = pattern.Replace(line, replacement);
                }

                line = Whitespace.Replace(line, " ").Trim();
                if (line.Length > 0) lines.Add(line);
            }

            return string.Join(singleLine ? " " : "\n", lines);
        }
        catch (RegexMatchTimeoutException)
        {
            return text;
        }
    }
}
