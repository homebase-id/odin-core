using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Odin.Services.PublicPage.Posts;

namespace Odin.Services.Tests.PublicPage;

public class TiptapRichTextParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "PublicPage", "Fixtures", name));

    private static string? Url(string key, string? fileId) => $"https://x.test/payload?key={key}";

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    [Test]
    public void AllBlocks_RenderSemanticHtml()
    {
        var html = TiptapRichTextParser.Parse(Fixture("tiptap-all-blocks.json"), Url)!;

        Assert.That(html, Does.Contain("<h1 id=\"every-block\">Every block</h1>"));
        Assert.That(html, Does.Contain("<h3 id=\"a-third-level-heading\">A third-level heading</h3>"));
        Assert.That(html, Does.Contain("<strong>bold</strong>"));
        Assert.That(html, Does.Contain("<u><em> italic</em></u>"));
        Assert.That(html, Does.Contain("<s> strike</s>"));
        Assert.That(html, Does.Contain("<kbd> Ctrl</kbd>"));
        Assert.That(html, Does.Contain("<sup>2</sup>"));
        Assert.That(html, Does.Contain("<sub>2</sub>"));
        Assert.That(html, Does.Contain("<a href=\"https://example.com\">a link</a>"));
        Assert.That(html, Does.Contain("<a href=\"https://frodo.dotyou.cloud\">@frodo.dotyou.cloud</a>"));
        Assert.That(html, Does.Contain("<sup><a href=\"#fn-fn1\" id=\"fnref-fn1\">[1]</a></sup>"));
        Assert.That(html, Does.Contain("<code>E = mc^2</code>"));
        Assert.That(html, Does.Contain("<pre>\\int_0^1 x\\,dx</pre>"));
        Assert.That(html, Does.Contain("<blockquote><p>A quote</p>"));
        Assert.That(html, Does.Contain("<pre><code class=\"language-ts\">const a = 1;\nconst b = 2;</code></pre>"));
        Assert.That(html, Does.Contain("<li><p>two</p>\n<ol><li>"));
        Assert.That(html, Does.Contain("<li>&#9745; <p>done</p>"));
        Assert.That(html, Does.Contain("<li>&#9744; <p>to do</p>"));
        Assert.That(html, Does.Contain("<th><p>Name</p>"));
        Assert.That(html, Does.Contain("<td><p>a</p>"));
        Assert.That(html, Does.Contain("<aside class=\"callout callout-warning\">"));
        Assert.That(html, Does.Contain("<details><summary>More</summary>"));
        Assert.That(html, Does.Contain(
            "<figure class=\"align-left\"><img src=\"https://x.test/payload?key=pst_rtei0\" alt=\"A photo\" width=\"320\" /><figcaption>Its caption</figcaption></figure>"));
        Assert.That(html, Does.Contain("<div class=\"gallery gallery-grid\">"));
        Assert.That(html, Does.Contain("key=pst_rtei2"));
        Assert.That(html, Does.Contain("<a href=\"https://example.com/post\">Example post</a><p>A description</p>"));
        Assert.That(html, Does.Contain("<hr/>"));
        Assert.That(html, Does.Contain("<li id=\"fn-fn1\"><p>The note</p>"));
        Assert.That(html, Does.Not.Contain("<p><figure"));
    }

    [Test]
    public void HeadingIds_AreDeduplicated()
    {
        var json = """
                   {"type":"doc","content":[
                     {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Hello, World!"}]},
                     {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Hello World"}]},
                     {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"hello  world"}]}]}
                   """;
        var html = TiptapRichTextParser.Parse(json)!;

        Assert.That(html, Does.Contain("<h2 id=\"hello-world\">"));
        Assert.That(html, Does.Contain("<h2 id=\"hello-world-2\">"));
        Assert.That(html, Does.Contain("<h2 id=\"hello-world-3\">"));
    }

    [Test]
    public void UnsafeContent_IsEncodedOrDropped()
    {
        var json = """
                   {"type":"doc","content":[
                     {"type":"paragraph","content":[
                       {"type":"text","text":"<script>alert(1)</script>"},
                       {"type":"text","text":"bad","marks":[{"type":"link","attrs":{"href":"javascript:alert(1)"}}]},
                       {"type":"text","text":"mail","marks":[{"type":"link","attrs":{"href":"mailto:a@b.co"}}]}]},
                     {"type":"mystery","content":[{"type":"paragraph","content":[{"type":"text","text":"kept"}]}]}]}
                   """;
        var html = TiptapRichTextParser.Parse(json)!;

        Assert.That(html, Does.Not.Contain("<script>"));
        Assert.That(html, Does.Contain("&lt;script&gt;"));
        Assert.That(html, Does.Not.Contain("javascript:"));
        Assert.That(html, Does.Contain("<a href=\"mailto:a@b.co\">mail</a>"));
        Assert.That(html, Does.Contain("<p>kept</p>"));
    }

    private const string Body = """[{"type":"p","children":[{"text":"slate text"}]}]""";

    private static BodyDoc Doc(string hash, string format = "tiptap-v1") => new()
    {
        Format = format,
        SlateHash = hash,
        Doc = JsonDocument.Parse(
            """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"tiptap text"}]}]}""").RootElement
    };

    [Test]
    public void ArticleBody_HashMatch_RendersTiptapDoc()
    {
        var html = ArticleBodyRenderer.Render(Body, Doc(Hash(Body)))!;
        Assert.That(html, Does.Contain("tiptap text"));
        Assert.That(html, Does.Not.Contain("slate text"));
    }

    [Test]
    public void ArticleBody_HashMismatch_FallsBackToSlate()
    {
        var html = ArticleBodyRenderer.Render(Body, Doc(Hash("something else")))!;
        Assert.That(html, Does.Contain("slate text"));
        Assert.That(html, Does.Not.Contain("tiptap text"));
    }

    [Test]
    public void ArticleBody_UnknownFormatOrNoDoc_FallsBackToSlate()
    {
        Assert.That(ArticleBodyRenderer.Render(Body, Doc(Hash(Body), "tiptap-v2")), Does.Contain("slate text"));
        Assert.That(ArticleBodyRenderer.Render(Body, null), Does.Contain("slate text"));
    }

    [Test]
    public void PostContent_BodyDocDeserializesAndHashesRawBodyText()
    {
        var hash = Hash(Body);
        var json = "{\"type\":\"Article\",\"body\":" + Body + ",\"bodyDoc\":{\"format\":\"tiptap-v1\",\"slateHash\":\"" + hash +
                   "\",\"doc\":{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"tiptap text\"}]}]}}}";
        var content = Odin.Core.Serialization.OdinSystemSerializer.Deserialize<PostContent>(json)!;

        var html = ArticleBodyRenderer.Render(Convert.ToString(content.Body)!, content.BodyDoc);
        Assert.That(html, Does.Contain("tiptap text"));
    }
}
