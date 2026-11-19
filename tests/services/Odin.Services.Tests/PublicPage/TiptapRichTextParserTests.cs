using System.IO;
using System.Linq;
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

    private static string? Excerpt(string json, int maxLength) =>
        TiptapRichTextParser.Excerpt(JsonDocument.Parse(json).RootElement, maxLength);

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
        Assert.That(html, Does.Contain("<code class=\"math\">E = mc^2</code>"));
        Assert.That(html, Does.Contain("<pre class=\"math\">\\int_0^1 x\\,dx</pre>"));
        Assert.That(html, Does.Contain("<blockquote><p>A quote</p>"));
        Assert.That(html, Does.Contain("<pre><code class=\"language-ts\">const a = 1;\nconst b = 2;</code></pre>"));
        Assert.That(html, Does.Contain("<li><p>two</p>\n<ol><li>"));
        Assert.That(html, Does.Contain("<li><p>&#9745; done</p>"));
        Assert.That(html, Does.Contain("<li><p>&#9744; to do</p>"));
        Assert.That(html, Does.Contain("<th><p>Name</p>"));
        Assert.That(html, Does.Contain("<td><p>a</p>"));
        Assert.That(html, Does.Contain("<aside class=\"callout callout-warning\" role=\"note\" aria-label=\"Warning\">"));
        Assert.That(html, Does.Contain("<details><summary>More</summary>"));
        Assert.That(html, Does.Contain(
            "<figure class=\"align-left\"><img src=\"https://x.test/payload?key=pst_rtei0\" alt=\"A photo\" width=\"320\" /><figcaption>Its caption</figcaption></figure>"));
        Assert.That(html, Does.Contain("<div class=\"gallery\">"));
        Assert.That(html, Does.Contain("key=pst_rtei2"));
        Assert.That(html, Does.Contain("<a href=\"https://example.com/post\">Example post</a><p>A description</p>"));
        Assert.That(html, Does.Contain("<hr/>"));
        Assert.That(html, Does.Contain("<li id=\"fn_fn1\" value=\"1\"><p>The note</p>"));
        Assert.That(html, Does.Not.Contain("<p><figure"));
    }

    [Test]
    public void HeadingIds_AreDeduplicated_InLinearTime_SkippingLiteralSuffixes()
    {
        var h = """{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"a"}]}""";
        var literal = """{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"A 2"}]}""";
        var json = $$"""{"type":"doc","content":[{{literal}},{{string.Join(",", Enumerable.Repeat(h, 20000))}}]}""";

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var html = Render(json)!;

        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(2)); // was 11.5 s when the suffix search restarted at 2
        Assert.That(html, Does.Contain("<h2 id=\"a-2\">A 2</h2>"));
        Assert.That(html, Does.Contain("<h2 id=\"a\">a</h2>"));
        Assert.That(html, Does.Contain("<h2 id=\"a-3\">a</h2>"));
        Assert.That(html, Does.Contain("<h2 id=\"a-20001\">a</h2>"));
        Assert.That(html, Does.Not.Contain("id=\"a-2\">a</h2>"));
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
        Assert.That(html, Does.Contain("<li id=\"fn_1\" value=\"1\">"));
        Assert.That(html, Does.Contain("[1]"));
        Assert.That(html, Does.Contain("[2]"));
        Assert.That(html, Does.Contain("[3]"));
        Assert.That(System.Text.RegularExpressions.Regex.Matches(html, "id=\"fn_noid\\.\\d\"").Count, Is.EqualTo(2));
        Assert.That(System.Text.RegularExpressions.Regex.Matches(html, "id=\"fnref_noid\\.\\d\"").Count, Is.EqualTo(2));
    }

    [Test]
    public void LoneSurrogate_DropsOnlyThatString()
    {
        // what JSON.stringify writes for an emoji cut in half
        var json = """
                   {"type":"doc","content":[
                     {"type":"paragraph","content":[{"type":"text","text":"cut \ud83d"}]},
                     {"type":"paragraph","content":[{"type":"text","text":"kept"}]}]}
                   """;

        Assert.That(Render(json), Is.EqualTo("<p></p>\n<p>kept</p>\n"));
    }

    [Test]
    public void JournalArticle_RendersEveryNodeAndAttr()
    {
        var html = Render(Fixture("journal-article.json"), Url)!;

        // alignment and indent are presentation, left to the JS viewer
        Assert.That(html, Does.Contain("<h1 id=\"field-notes\">Field notes</h1>"));
        Assert.That(html, Does.Contain("<h2 id=\"indented-heading\">"));
        Assert.That(html, Does.Contain("<p>Read "));
        Assert.That(html, Does.Not.Contain("style="));

        // a note link is its label, as text
        Assert.That(html, Does.Contain("Read My &lt;b&gt;other&lt;/b&gt; note and "));
        Assert.That(html, Does.Not.Contain("7f1c2e0a"));

        // marks
        Assert.That(html, Does.Contain("<a href=\"https://example.com/a?b=1&amp;c=&quot;2&quot;\">this</a>"));
        Assert.That(html, Does.Contain("<u>, not </u>"));
        Assert.That(html, Does.Contain("<s> or </s>"));
        Assert.That(html, Does.Contain("<code> or </code>"));
        Assert.That(html, Does.Contain("H<sub>2</sub>O and x<sup>2</sup>"));
        Assert.That(html, Does.Contain("<code class=\"math\">a&lt;b &amp;&amp; &quot;c&quot;</code>"));
        Assert.That(html, Does.Contain("<pre class=\"math\">\\frac{1}{2} &lt; x</pre>"));

        // nested task lists
        Assert.That(html, Does.Contain("<li><p>&#9745; outer done</p>\n<ul class=\"task-list\"><li><p>&#9744; inner to do</p>"));

        // tables keep a real colspan and drop one that is not a number
        Assert.That(html, Does.Contain("<th colspan=\"2\"><p>Wide header</p>"));
        Assert.That(html, Does.Contain("<td><p>right</p>"));

        // images: the paragraph that holds them is a div; caption null shows the alt, "" shows none
        Assert.That(html, Does.Contain(
            "<div><figure class=\"align-right\"><img src=\"https://x.test/payload?key=pst_rtei0\" alt=\"Alt &quot;quoted&quot;\" width=\"240\" /><figcaption>Alt &quot;quoted&quot;</figcaption></figure>"));
        Assert.That(html, Does.Contain("<figure><img src=\"https://x.test/payload?key=pst_rtei1\" alt=\"Hidden caption\" /></figure>"));
        Assert.That(html, Does.Contain("<figcaption>&lt;script&gt;alert(1)&lt;/script&gt;</figcaption>"));
        Assert.That(html, Does.Not.Contain("bad src"));

        // galleries keep their images; the layout is presentation, left to the JS viewer
        Assert.That(html, Does.Contain("<div class=\"gallery\"><figure class=\"align-left\"><img src=\"https://x.test/payload?key=pst_rtei3\" alt=\"M1\" width=\"500\" />"));
        Assert.That(html, Does.Contain("<div class=\"gallery\"><figure><img src=\"https://x.test/payload?key=pst_rtei7\""));
        Assert.That(html, Does.Not.Contain("gallery-"));

        // link previews
        Assert.That(html, Does.Contain(
            "<div class=\"link-preview\"><a href=\"https://journal.example/post\">A &quot;great&quot; &lt;post&gt;</a><p>&lt;img src=x onerror=alert(1)&gt;</p></div>"));
        Assert.That(html, Does.Contain("<a href=\"https://no-title.example/path\">no-title.example</a></div>"));
        Assert.That(html, Does.Not.Contain("Bad preview"));

        // toggles, callouts
        Assert.That(html, Does.Contain("<details><summary>Details</summary><p>toggle body</p>"));
        Assert.That(html, Does.Contain("<summary>&lt;/summary&gt;&lt;script&gt;alert(1)&lt;/script&gt;</summary>"));
        Assert.That(html, Does.Contain("<aside class=\"callout callout-tip\" role=\"note\" aria-label=\"Tip\"><p>A tip</p>"));
        Assert.That(html, Does.Contain("<aside class=\"callout callout-error\" role=\"note\" aria-label=\"Error\">"));
        Assert.That(html, Does.Contain("<aside class=\"callout callout-info\" role=\"note\" aria-label=\"Info\"><p>Bad variant</p>"));

        // footnotes: ids keep word characters and '-' only, like the viewer
        Assert.That(html, Does.Contain("<sup><a href=\"#fn_n1onclickx\" id=\"fnref_n1onclickx\">[1]</a></sup>"));
        Assert.That(html, Does.Contain("<li id=\"fn_n1onclickx\" value=\"1\"><p>The footnote</p>\n <a href=\"#fnref_n1onclickx\" aria-label=\"Back to reference\">&#8617;</a></li>"));

        // nodes this renderer does not know keep their text
        Assert.That(html, Does.Contain("<p>future text kept</p>"));
        Assert.That(html, Does.Contain("loose &lt;text&gt;"));
    }

    [Test]
    public void JournalArticle_LiveBlocksAreEscapedSource()
    {
        var html = Render(Fixture("journal-article.json"), Url)!;

        Assert.That(html, Does.Contain(
            "<pre><code class=\"language-html\">&lt;div id=&quot;app&quot;&gt;&lt;/div&gt;\n&lt;script&gt;alert(&#39;pwned&#39;)&lt;/script&gt;</code></pre>"));
        Assert.That(html, Does.Contain("<pre><code class=\"language-react\">"));
        Assert.That(html, Does.Contain("<pre><code class=\"language-mermaid\">graph TD; A--&gt;B</code></pre>"));
        Assert.That(html, Does.Contain("<code class=\"language-svg\">&lt;svg onload=&quot;alert(1)&quot;&gt;"));
        Assert.That(html, Does.Not.Contain("Interactive block"));
        // the rest of the info string is not the language
        Assert.That(html, Does.Not.Contain("k3f9"));
        Assert.That(html, Does.Not.Contain("wide"));
        // a hostile language is cut down to safe class characters
        Assert.That(html, Does.Contain("<pre><code class=\"language-scriptalert1script\">evil language</code></pre>"));
        Assert.That(html, Does.Contain("<pre><code>plain code</code></pre>"));
    }

    [Test]
    public void JournalArticle_EmitsNoScriptOrUnsafeMarkup()
    {
        var html = Render(Fixture("journal-article.json"), Url)!;

        Assert.That(html, Does.Not.Contain("<script"));
        Assert.That(html, Does.Not.Contain("<svg"));
        Assert.That(html, Does.Not.Contain("<b>"));
        Assert.That(html, Does.Not.Contain("<img src=x"));
        Assert.That(html, Does.Not.Contain("javascript:").IgnoreCase);
        Assert.That(html, Does.Not.Contain("data:text"));
        // no event handler attribute of any name
        Assert.That(html, Does.Not.Match(@"<[^>]*\son\w+\s*="));
        // the unsafe links keep their text
        Assert.That(html, Does.Contain("</u>that<s>"));
        Assert.That(html, Does.Contain("</code>data. H"));
    }

    [TestCase("""{"type":"doc","content":[{"type":"text","text":"a"},{"type":"text","text":"b"}],"content":[]}""")]
    [TestCase("""{"type":"doc","content":[{"type":"paragraph","attrs":{"indent":"x","textAlign":5},"content":"no"}]}""")]
    [TestCase("""{"type":"doc","content":[{"type":"codeBlock","attrs":{"language":["html"]},"content":[{"type":"text","text":"<b>"}]}]}""")]
    [TestCase("""{"type":"doc","content":[{"type":"noteLink","attrs":{"label":7}},{"type":"image","attrs":{"width":"1e400"}}]}""")]
    [TestCase("""[]""")]
    public void OddDocuments_NeverThrow(string json)
    {
        Assert.DoesNotThrow(() => Render(json));
        Assert.DoesNotThrow(() => Excerpt(json, 160));
    }

    [Test]
    public void Excerpt_IsProseOnly()
    {
        var excerpt = Excerpt(Fixture("journal-article.json"), 1000)!;

        Assert.That(excerpt, Does.StartWith("Field notes Indented heading Read My <b>other</b> note and this, not that"));
        Assert.That(excerpt, Does.Contain("outer done inner to do"));
        Assert.That(excerpt, Does.Contain("inner to do Wide header left right toggle body"));
        Assert.That(excerpt, Does.Contain("A tip"));
        Assert.That(excerpt, Does.Contain("future text kept"));
        // no code (live blocks or not), math, captions, previews or footnotes
        Assert.That(excerpt, Does.Not.Contain("pwned"));
        Assert.That(excerpt, Does.Not.Contain("graph TD"));
        Assert.That(excerpt, Does.Not.Contain("plain code"));
        Assert.That(excerpt, Does.Not.Contain("frac"));
        Assert.That(excerpt, Does.Not.Contain("a<b"));
        Assert.That(excerpt, Does.Not.Contain("Alt"));
        Assert.That(excerpt, Does.Not.Contain("great"));
        Assert.That(excerpt, Does.Not.Contain("The footnote"));
        Assert.That(excerpt, Does.Not.Contain("  "));
    }

    [Test]
    public void Excerpt_CutsAtAWordWithAnEllipsis()
    {
        var excerpt = Excerpt(Fixture("journal-article.json"), 40)!;
        Assert.That(excerpt.Length, Is.LessThanOrEqualTo(40));
        Assert.That(excerpt, Is.EqualTo("Field notes Indented heading Read My…"));
    }

    [Test]
    public void Excerpt_OfALiveBlockOnlyDocument_IsNull()
    {
        var json = """{"type":"doc","content":[{"type":"codeBlock","attrs":{"language":"html id=abcd"},"content":[{"type":"text","text":"<p>lots of html</p>"}]}]}""";
        Assert.That(Excerpt(json, 160), Is.Null);
    }

    [Test]
    public void Footnotes_AreNumberedByReferenceOrder_WhereverTheSectionIs()
    {
        var html = Render("""
            {"type":"doc","content":[
              {"type":"footnotes","content":[
                {"type":"footnote","attrs":{"id":"b"},"content":[{"type":"paragraph","content":[{"type":"text","text":"B"}]}]},
                {"type":"footnote","attrs":{"id":"a"},"content":[{"type":"paragraph","content":[{"type":"text","text":"A"}]}]}]},
              {"type":"paragraph","content":[{"type":"text","text":"x"},{"type":"footnoteReference","attrs":{"id":"a"}},
                {"type":"footnoteReference","attrs":{"id":"b"}}]}]}
            """)!;
        Assert.That(html, Does.Contain("<li id=\"fn_b\" value=\"2\">"));
        Assert.That(html, Does.Contain("<li id=\"fn_a\" value=\"1\">"));
        Assert.That(html, Does.Contain("href=\"#fnref_b\""));
    }

    [Test]
    public void NoteLink_KeepsItsMarks()
    {
        var html = Render("""
            {"type":"doc","content":[{"type":"paragraph","content":[
              {"type":"noteLink","attrs":{"noteId":"n","label":"Other <note>"},"marks":[{"type":"bold"}]}]}]}
            """)!;
        Assert.That(html, Does.Contain("<strong>Other &lt;note&gt;</strong>"));
    }

    [Test]
    public void Excerpt_PutsNoSpaceBeforeInlineAtoms_AndCollapsesWhitespaceAsItGoes()
    {
        var excerpt = Excerpt("""
            {"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Water boils"},
              {"type":"footnoteReference","attrs":{"id":"a"}},{"type":"text","text":", usually"}]}]}
            """, 160);
        Assert.That(excerpt, Is.EqualTo("Water boils, usually"));

        var spaced = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"" +
                     new string(' ', 500) + "prose\"}]}]}";
        Assert.That(Excerpt(spaced, 160), Is.EqualTo("prose"));
    }
}
