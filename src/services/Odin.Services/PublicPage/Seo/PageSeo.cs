using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Odin.Services.PublicPage.Seo;

/// <summary>Article fields that feed article:* meta tags and the BlogPosting JSON-LD</summary>
public class ArticleSeo
{
    public string Headline { get; init; }
    public string Description { get; init; }
    public DateTimeOffset Published { get; init; }
    public DateTimeOffset Modified { get; init; }
    public string AuthorName { get; init; }
    public string AuthorUrl { get; init; }
    public int? WordCount { get; init; }
}

public static class PageSeo
{
    /// <summary>
    /// Open Graph, Twitter card and (for articles) article:* tags plus BlogPosting JSON-LD.
    /// All values are encoded here; callers pass raw strings.
    /// </summary>
    public static string BuildMetaTags(
        string title, string description, string siteName, string ogType,
        string pageUrl, string imageUrl, ArticleSeo article)
    {
        static string E(string s) => HttpUtility.HtmlEncode(s ?? "");

        var hasImage = !string.IsNullOrEmpty(imageUrl);
        var b = new StringBuilder(1024);

        b.AppendLine($"<meta property='og:title' content='{E(title)}'/>");
        b.AppendLine($"<meta property='og:description' content='{E(description)}'/>");
        b.AppendLine($"<meta property='og:url' content='{E(pageUrl)}'/>");
        b.AppendLine($"<meta property='og:site_name' content='{E(siteName)}'/>");
        b.AppendLine($"<meta property='og:type' content='{E(article != null ? "article" : ogType)}'/>");
        if (hasImage)
        {
            b.AppendLine($"<meta property='og:image' content='{E(imageUrl)}'/>");
        }

        b.AppendLine($"<meta name='twitter:card' content='{(hasImage ? "summary_large_image" : "summary")}'/>");
        b.AppendLine($"<meta name='twitter:title' content='{E(title)}'/>");
        b.AppendLine($"<meta name='twitter:description' content='{E(description)}'/>");
        if (hasImage)
        {
            b.AppendLine($"<meta name='twitter:image' content='{E(imageUrl)}'/>");
        }

        if (article != null)
        {
            b.AppendLine($"<meta property='article:published_time' content='{Iso(article.Published)}'/>");
            b.AppendLine($"<meta property='article:modified_time' content='{Iso(article.Modified)}'/>");
            b.AppendLine($"<meta property='article:author' content='{E(article.AuthorUrl)}'/>");
            b.AppendLine("<script type='application/ld+json'>");
            b.AppendLine(BuildBlogPostingJson(article, pageUrl, imageUrl));
            b.AppendLine("</script>");
        }

        return b.ToString();
    }

    public static string BuildBlogPostingJson(ArticleSeo a, string pageUrl, string imageUrl)
    {
        using var stream = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("@context", "https://schema.org");
            w.WriteString("@type", "BlogPosting");
            w.WriteString("headline", a.Headline ?? "");
            if (!string.IsNullOrEmpty(a.Description)) w.WriteString("description", a.Description);
            w.WriteString("datePublished", Iso(a.Published));
            w.WriteString("dateModified", Iso(a.Modified));
            w.WriteStartObject("author");
            w.WriteString("@type", "Person");
            w.WriteString("name", a.AuthorName ?? "");
            w.WriteString("url", a.AuthorUrl ?? "");
            w.WriteEndObject();
            if (!string.IsNullOrEmpty(imageUrl)) w.WriteString("image", imageUrl);
            if (a.WordCount is > 0) w.WriteNumber("wordCount", a.WordCount.Value);
            w.WriteString("mainEntityOfPage", pageUrl);
            w.WriteEndObject();
        }

        // The default encoder escapes < > & ' as \uXXXX, so the text can never contain </script>.
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Iso(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
