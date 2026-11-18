using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Odin.Services.PublicPage.Posts;

namespace Odin.Services.Tests.PublicPage;

public class TiptapRichTextParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "PublicPage", "Fixtures", name));

    private static string? Url(string key, string? fileId) =>
        fileId == null ? $"https://x.test/payload?key={key}" : $"https://x.test/payload?fileId={fileId}&key={key}";

    private static string? Render(string json, PayloadUrlResolver? url = null) =>
        TiptapRichTextParser.Parse(JsonDocument.Parse(json).RootElement, url);

    [Test]
    public void AllBlocks_RenderSemanticHtml()
    {
        var html = Render(Fixture("tiptap-all-blocks.json"), Url)!;

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
        Assert.That(html, Does.Contain("<sup><a href=\"#fn_fn1\" id=\"fnref_fn1\">[1]</a></sup>"));
        Assert.That(html, Does.Contain("<code>E = mc^2</code>"));
        Assert.That(html, Does.Contain("<pre>\\int_0^1 x\\,dx</pre>"));
        Assert.That(html, Does.Contain("<blockquote><p>A quote</p>"));
        Assert.That(html, Does.Contain("<pre><code class=\"language-ts\">const a = 1;\nconst b = 2;</code></pre>"));
        Assert.That(html, Does.Contain("<li><p>two</p>\n<ol><li>"));
        Assert.That(html, Does.Contain("<li><p>&#9745; done</p>"));
        Assert.That(html, Does.Contain("<li><p>&#9744; to do</p>"));
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
        Assert.That(html, Does.Contain("<li id=\"fn_fn1\"><p>The note</p>"));
        Assert.That(html, Does.Not.Contain("<p><figure"));
    }

    [Test]
    public void HeadingIds_AreDeduplicated()
    {
        var json = """
                   {"type":"doc","content":[
                     {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Hello, World!"}]},
                     {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Hello World"}]},
                     {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"hello world 3"}]},
                     {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"hello  world"}]}]}
                   """;
        var html = Render(json)!;

        Assert.That(html, Does.Contain("<h2 id=\"hello-world\">"));
        Assert.That(html, Does.Contain("<h2 id=\"hello-world-2\">"));
        Assert.That(html, Does.Contain("<h2 id=\"hello-world-3\">"));
        // the literal hello-world-3 is taken, so the next repeat skips it
        Assert.That(html, Does.Contain("<h2 id=\"hello-world-4\">"));
    }

    [Test]
    public void UnsafeContent_IsEncodedOrDropped()
    {
        var json = """
                   {"type":"doc","content":[
                     {"type":"paragraph","content":[
                       {"type":"text","text":"<script>alert(1)</script>"},
                       {"type":"text","text":"bad","marks":[{"type":"link","attrs":{"href":"javascript:alert(1)"}}]},
                       {"type":"text","text":"mail","marks":[{"type":"link","attrs":{"href":"mailto:a@b.co"}}]},
                       {"type":"text","text":"anchor","marks":[{"type":"link","attrs":{"href":"#notes"}}]},
                       {"type":"text","text":"path","marks":[{"type":"link","attrs":{"href":"/posts/blog/a?x=1"}}]},
                       {"type":"text","text":"rel","marks":[{"type":"link","attrs":{"href":"../a.html"}}]},
                       {"type":"text","text":"x1","marks":[{"type":"link","attrs":{"href":"//evil.test/a"}}]},
                       {"type":"text","text":"x2","marks":[{"type":"link","attrs":{"href":"/\\evil.test"}}]},
                       {"type":"text","text":"x3","marks":[{"type":"link","attrs":{"href":"data:text/html,<b>"}}]},
                       {"type":"text","text":"x4","marks":[{"type":"link","attrs":{"href":"vbscript:msgbox"}}]},
                       {"type":"text","text":"x5","marks":[{"type":"link","attrs":{"href":"file:///etc/passwd"}}]},
                       {"type":"text","text":"x6","marks":[{"type":"link","attrs":{"href":"java\tscript:alert(1)"}}]}]},
                     {"type":"mystery","content":[{"type":"paragraph","content":[{"type":"text","text":"kept"}]}]}]}
                   """;
        var html = Render(json)!;

        Assert.That(html, Does.Not.Contain("<script>"));
        Assert.That(html, Does.Contain("&lt;script&gt;"));
        Assert.That(html, Does.Not.Contain("javascript:"));
        Assert.That(html, Does.Contain("<a href=\"mailto:a@b.co\">mail</a>"));
        Assert.That(html, Does.Contain("<a href=\"#notes\">anchor</a>"));
        Assert.That(html, Does.Contain("<a href=\"/posts/blog/a?x=1\">path</a>"));
        Assert.That(html, Does.Contain("<a href=\"../a.html\">rel</a>"));
        Assert.That(html, Does.Contain("x1x2x3x4x5x6"));
        Assert.That(html, Does.Contain("<p>kept</p>"));
    }

    [TestCase("Héllo wörld — 日本語", "hello-world-日本語")]
    [TestCase("日本語の見出し", "日本語の見出し")]
    [TestCase("Привет, мир! №1", "привет-мир-no1")]
    [TestCase("ﬁle ①", "file-1")]
    [TestCase(" — !! ", "section")]
    [TestCase("  --Already-Slugged--  ", "already-slugged")]
    [TestCase("", "section")]
    [TestCase("\uD800 lone", "lone")]
    public void BaseSlug_MatchesTheViewer(string text, string expected)
    {
        Assert.That(TiptapRichTextParser.BaseSlug(text), Is.EqualTo(expected));
    }

    [Test]
    public void Image_WithFileId_ResolvesAgainstThatFile()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                     {"type":"image","attrs":{"fileKey":"own","fileId":null}},
                     {"type":"image","attrs":{"fileKey":"copied","fileId":"f2"}}]}]}
                   """;
        var html = Render(json, Url)!;

        Assert.That(html, Does.Contain("src=\"https://x.test/payload?key=own\""));
        Assert.That(html, Does.Contain("src=\"https://x.test/payload?fileId=f2&amp;key=copied\""));
    }

    [Test]
    public void TaskItem_PutsTheBoxInsideItsFirstParagraph()
    {
        var json = """
                   {"type":"doc","content":[{"type":"taskList","content":[
                     {"type":"taskItem","attrs":{"checked":true},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"first"}]},
                       {"type":"paragraph","content":[{"type":"text","text":"second"}]}]},
                     {"type":"taskItem","attrs":{"checked":false},"content":[
                       {"type":"bulletList","content":[]}]},
                     {"type":"taskItem","attrs":{"checked":false}}]}]}
                   """;
        var html = Render(json)!;

        Assert.That(html, Does.Contain("<li><p>&#9745; first</p>\n<p>second</p>\n</li>"));
        Assert.That(html, Does.Contain("<li>&#9744; <ul></ul>\n</li>"));
        Assert.That(html, Does.Contain("<li>&#9744; </li>"));
    }

    [Test]
    public void Footnotes_NeverShareAnAnchorWithAHeadingOrEachOther()
    {
        var json = """
                   {"type":"doc","content":[
                     {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"fn 1"}]},
                     {"type":"paragraph","content":[
                       {"type":"footnoteReference","attrs":{"id":"1"}},
                       {"type":"footnoteReference","attrs":{}},
                       {"type":"footnoteReference"}]},
                     {"type":"footnotes","content":[
                       {"type":"footnote","attrs":{"id":"1"}},
                       {"type":"footnote"},
                       {"type":"footnote","attrs":{"id":""}}]}]}
                   """;
        var html = Render(json)!;

        Assert.That(html, Does.Contain("<h2 id=\"fn-1\">"));
        Assert.That(html, Does.Contain("<li id=\"fn_1\">"));
        Assert.That(html, Does.Contain("[1]"));
        Assert.That(html, Does.Contain("[2]"));
        Assert.That(html, Does.Contain("[3]"));
        Assert.That(System.Text.RegularExpressions.Regex.Matches(html, "id=\"fn_noid\\.\\d\"").Count, Is.EqualTo(2));
        Assert.That(System.Text.RegularExpressions.Regex.Matches(html, "id=\"fnref_noid\\.\\d\"").Count, Is.EqualTo(2));
    }
}
