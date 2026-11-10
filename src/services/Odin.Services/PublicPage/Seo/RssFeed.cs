using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security;
using System.Text;

namespace Odin.Services.PublicPage.Seo;

public record RssItem(string Title, string Link, string Description, DateTimeOffset Published);

public static class RssFeed
{
    public static string Build(string title, string siteUrl, string description, string feedUrl, IEnumerable<RssItem> items)
    {
        static string X(string s) => SecurityElement.Escape(s ?? "");

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
