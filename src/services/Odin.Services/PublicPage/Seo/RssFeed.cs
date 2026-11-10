using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security;
using System.Text;
using System.Xml;

namespace Odin.Services.PublicPage.Seo;

public record RssItem(string Title, string Link, string Description, DateTimeOffset Published);

public static class RssFeed
{
    /// <summary>https://{host}/posts/{channel}/{post}, each path segment escaped, the way the sitemap builds its URLs</summary>
    public static string PostLink(string host, string channelSlug, string postId)
    {
        return new UriBuilder
        {
            Scheme = "https",
            Host = host,
            Path = $"/posts/{Uri.EscapeDataString(channelSlug ?? "")}/{Uri.EscapeDataString(postId ?? "")}"
        }.Uri.AbsoluteUri;
    }

    /// <summary>Drops the characters XML 1.0 cannot hold at all (most control characters, lone surrogates, U+FFFE/U+FFFF)</summary>
    public static string StripInvalidXmlChars(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";

        StringBuilder b = null;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (XmlConvert.IsXmlChar(c))
            {
                b?.Append(c);
                continue;
            }

            if (i + 1 < s.Length && XmlConvert.IsXmlSurrogatePair(s[i + 1], c))
            {
                b?.Append(c).Append(s[i + 1]);
                i++;
                continue;
            }

            b ??= new StringBuilder(s, 0, i, s.Length);
        }

        return b?.ToString() ?? s;
    }

    public static string Build(string title, string siteUrl, string description, string feedUrl, IEnumerable<RssItem> items)
    {
        static string X(string s) => SecurityElement.Escape(StripInvalidXmlChars(s));

        var b = new StringBuilder();
        b.AppendLine("<?xml version='1.0' encoding='UTF-8'?>");
        b.AppendLine("<rss version='2.0' xmlns:atom='http://www.w3.org/2005/Atom'>");
        b.AppendLine("<channel>");
        b.AppendLine($"<title>{X(title)}</title>");
        b.AppendLine($"<link>{X(siteUrl)}</link>");
        b.AppendLine($"<description>{X(description)}</description>");
        b.AppendLine($"<atom:link href='{X(feedUrl)}' rel='self' type='application/rss+xml'/>");
        foreach (var i in items)
        {
            b.AppendLine("<item>");
            b.AppendLine($"<title>{X(i.Title)}</title>");
            b.AppendLine($"<link>{X(i.Link)}</link>");
            b.AppendLine($"<guid isPermaLink='true'>{X(i.Link)}</guid>");
            b.AppendLine($"<pubDate>{i.Published.ToString("r", CultureInfo.InvariantCulture)}</pubDate>");
            if (!string.IsNullOrEmpty(i.Description))
            {
                b.AppendLine($"<description>{X(i.Description)}</description>");
            }

            b.AppendLine("</item>");
        }

        b.AppendLine("</channel>");
        b.AppendLine("</rss>");
        return b.ToString();
    }
}
