using System;
using System.Text.Json;
using NUnit.Framework;
using Odin.Services.PublicPage.Seo;

namespace Odin.Services.Tests.PublicPage;

public class PageSeoTests
{
    private const string PageUrl = "https://frodo.dotyou.cloud/posts/blog/trip";

    private static readonly ArticleSeo Article = new()
    {
        Headline = "Trip </script><b>notes</b>",
        Description = "A short abstract",
        Published = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero),
        Modified = new DateTimeOffset(2026, 3, 2, 11, 30, 0, TimeSpan.Zero),
        AuthorName = "Frodo Baggins",
        AuthorUrl = "https://frodo.dotyou.cloud",
        WordCount = 420
    };

    [Test]
    public void Article_EmitsArticleOgTwitterAndBlogPosting()
    {
        var html = PageSeo.BuildMetaTags("Frodo | Trip", "A short abstract", "Frodo Baggins", "website",
            PageUrl, "https://frodo.dotyou.cloud/thumb.jpg?a=1&b=2", Article);

        Assert.That(html, Does.Contain("<meta property='og:type' content='article'/>"));
        Assert.That(html, Does.Contain("<meta property='og:site_name' content='Frodo Baggins'/>"));
        Assert.That(html, Does.Contain("content='https://frodo.dotyou.cloud/thumb.jpg?a=1&amp;b=2'"));
        Assert.That(html, Does.Contain("<meta name='twitter:card' content='summary_large_image'/>"));
        Assert.That(html, Does.Contain("<meta name='twitter:title' content='Frodo | Trip'/>"));
        Assert.That(html, Does.Contain("<meta name='twitter:image'"));
        Assert.That(html, Does.Contain("<meta property='article:published_time' content='2026-03-01T10:00:00Z'/>"));
        Assert.That(html, Does.Contain("<meta property='article:modified_time' content='2026-03-02T11:30:00Z'/>"));
        Assert.That(html, Does.Contain("<meta property='article:author' content='https://frodo.dotyou.cloud'/>"));

        var json = html.Substring(html.IndexOf("<script type='application/ld+json'>", StringComparison.Ordinal));
        json = json.Replace("<script type='application/ld+json'>", "");
        json = json.Substring(0, json.IndexOf("</script>", StringComparison.Ordinal));
        // the headline holds </script>; it must not appear raw, and the tag must close only once
        Assert.That(html.Split("</script>").Length, Is.EqualTo(2));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("@type").GetString(), Is.EqualTo("BlogPosting"));
        Assert.That(root.GetProperty("headline").GetString(), Is.EqualTo(Article.Headline));
        Assert.That(root.GetProperty("description").GetString(), Is.EqualTo("A short abstract"));
        Assert.That(root.GetProperty("datePublished").GetString(), Is.EqualTo("2026-03-01T10:00:00Z"));
        Assert.That(root.GetProperty("dateModified").GetString(), Is.EqualTo("2026-03-02T11:30:00Z"));
        Assert.That(root.GetProperty("author").GetProperty("name").GetString(), Is.EqualTo("Frodo Baggins"));
        Assert.That(root.GetProperty("author").GetProperty("url").GetString(), Is.EqualTo("https://frodo.dotyou.cloud"));
        Assert.That(root.GetProperty("wordCount").GetInt32(), Is.EqualTo(420));
        Assert.That(root.GetProperty("mainEntityOfPage").GetString(), Is.EqualTo(PageUrl));
        Assert.That(root.GetProperty("image").GetString(), Does.StartWith("https://frodo.dotyou.cloud/thumb.jpg"));
    }

    [Test]
    public void NonArticle_HasNoArticleTagsAndSummaryCardWithoutImage()
    {
        var html = PageSeo.BuildMetaTags("Frodo | About", "Bio", "Frodo Baggins", "profile", "https://frodo.dotyou.cloud/about", null, null);

        Assert.That(html, Does.Contain("<meta property='og:type' content='profile'/>"));
        Assert.That(html, Does.Contain("<meta property='og:site_name' content='Frodo Baggins'/>"));
        Assert.That(html, Does.Contain("<meta name='twitter:card' content='summary'/>"));
        Assert.That(html, Does.Not.Contain("og:image"));
        Assert.That(html, Does.Not.Contain("twitter:image"));
        Assert.That(html, Does.Not.Contain("article:"));
        Assert.That(html, Does.Not.Contain("BlogPosting"));
    }

    [Test]
    public void Article_WithoutOptionalFields_OmitsThem()
    {
        var a = new ArticleSeo { Headline = "H", Published = Article.Published, Modified = Article.Modified, AuthorName = "A", AuthorUrl = "https://a" };
        var json = PageSeo.BuildBlogPostingJson(a, PageUrl, null);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.TryGetProperty("wordCount", out _), Is.False);
        Assert.That(doc.RootElement.TryGetProperty("image", out _), Is.False);
        Assert.That(doc.RootElement.TryGetProperty("description", out _), Is.False);
    }

    [Test]
    public void Rss_ListsItemsAndEscapesText()
    {
        var xml = RssFeed.Build("Frodo & Co", "https://frodo.dotyou.cloud/", "desc", "https://frodo.dotyou.cloud/feed.xml",
        [
            new RssItem("A <b> post", "https://frodo.dotyou.cloud/posts/blog/a", "abstract", Article.Published)
        ]);

        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);
        Assert.That(doc.SelectSingleNode("/rss/channel/title")!.InnerText, Is.EqualTo("Frodo & Co"));
        Assert.That(doc.SelectSingleNode("/rss/channel/item/title")!.InnerText, Is.EqualTo("A <b> post"));
        Assert.That(doc.SelectSingleNode("/rss/channel/item/link")!.InnerText, Is.EqualTo("https://frodo.dotyou.cloud/posts/blog/a"));
        Assert.That(doc.SelectSingleNode("/rss/channel/item/pubDate")!.InnerText, Is.EqualTo("Sun, 01 Mar 2026 10:00:00 GMT"));
    }
}
