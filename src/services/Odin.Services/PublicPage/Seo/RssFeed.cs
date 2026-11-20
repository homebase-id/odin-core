using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace Odin.Services.PublicPage.Seo;

/// <param name="Id">A stable id for the guid, e.g. the post's fileId, so an edited slug is not a new item</param>
public record RssItem(string Title, string Link, string Id, string Description, DateTimeOffset Published);

public static class RssFeed
{
    /// <summary>https://{host}/posts/{channel}/{post}, each path segment escaped; the feed and the sitemap both use it</summary>
    public static string PostUrl(string host, string channelSlug, string postId)
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
        const string atom = "http://www.w3.org/2005/Atom";
        using var stream = new MemoryStream();
        using (var w = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            // XmlWriter escapes; it can't hold the characters XML has no place for, so they go first
            void Element(string name, string value) => w.WriteElementString(name, StripInvalidXmlChars(value));

            w.WriteStartDocument();
            w.WriteStartElement("rss");
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("xmlns", "atom", null, atom);
            w.WriteStartElement("channel");
            Element("title", title);
            Element("link", siteUrl);
            Element("description", description);
            w.WriteStartElement("link", atom);
            w.WriteAttributeString("href", StripInvalidXmlChars(feedUrl));
            w.WriteAttributeString("rel", "self");
            w.WriteAttributeString("type", "application/rss+xml");
            w.WriteEndElement();
            foreach (var i in items)
            {
                w.WriteStartElement("item");
                Element("title", i.Title);
                Element("link", i.Link);
                w.WriteStartElement("guid");
                w.WriteAttributeString("isPermaLink", "false");
                w.WriteString(StripInvalidXmlChars(i.Id));
                w.WriteEndElement();
                Element("pubDate", i.Published.ToString("r", CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(i.Description)) Element("description", i.Description);
                w.WriteEndElement();
            }

            w.WriteEndDocument();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
