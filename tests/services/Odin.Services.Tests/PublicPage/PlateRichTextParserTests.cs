using System;
using System.IO;
using NUnit.Framework;
using Odin.Services.PublicPage.Posts;

namespace Odin.Services.Tests.PublicPage;

public class PlateRichTextParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "PublicPage", "Fixtures", name));

    private static string? Url(string key) => $"https://x.test/payload?key={key}";

    [Test]
    public void EditorArticle_RendersAllKnownNodes()
    {
        var html = PlateRichTextParser.Parse(Fixture("editor-article.json"), Url)!;

        Assert.That(html, Does.Contain("<h1>Trip notes</h1>"));
        Assert.That(html, Does.Contain("<h2>Packing</h2>"));
        Assert.That(html, Does.Contain("<strong>early</strong>"));
        Assert.That(html, Does.Contain("<em><strong>late</strong></em>"));
        Assert.That(html, Does.Contain("<u>zoom</u>"));
        Assert.That(html, Does.Contain("<s>pan</s>"));
        Assert.That(html, Does.Contain("<kbd>Ctrl</kbd>"));
        Assert.That(html, Does.Contain("<code>map.zoom()</code>"));
        Assert.That(html, Does.Contain("<a href=\"https://frodo.dotyou.cloud\">@frodo.dotyou.cloud</a>"));
        Assert.That(html, Does.Contain("<a href=\"https://example.com/map\">the route</a>"));
        Assert.That(html, Does.Contain("<ul>"));
        Assert.That(html, Does.Contain("<ol><li>Bread</li>"));
        Assert.That(html, Does.Contain("<li>Boots</li>"));
        Assert.That(html, Does.Contain("<blockquote>Not all those who wander are lost.</blockquote>"));
        Assert.That(html, Does.Contain("<pre><code>const pack = [&#39;boots&#39;];\n\npack.push(&#39;food&#39;);</code></pre>"));
        Assert.That(html, Does.Contain("<img src=\"https://x.test/payload?key=pst_rtei0\""));
        Assert.That(html, Does.Contain("<p>The end.</p>"));
        Assert.That(html, Does.Contain("Second line."));
    }

    [Test]
    public void ImporterArticle_DoesNotThrowAndKeepsText()
    {
        var html = PlateRichTextParser.Parse(Fixture("importer-article.json"), Url)!;

        Assert.That(html, Does.Contain("<a href=\"https://blog.example.com/post\">https://blog.example.com/post</a>"));
        Assert.That(html, Does.Contain("<a href=\"https://sam.dotyou.cloud\">@sam.dotyou.cloud</a>"));
        Assert.That(html, Does.Contain("<strong>bold</strong>"));
        Assert.That(html, Does.Contain("<em><strong>bold italic</strong></em>"));
        Assert.That(html, Does.Contain("<h3>A third-level heading</h3>"));
        Assert.That(html, Does.Contain("<figcaption>A caption</figcaption>"));
        Assert.That(html, Does.Contain("src=\"https://blog.example.com/remote.jpg\""));
        Assert.That(html, Does.Contain("Inside a div"));
        Assert.That(html, Does.Contain("<blockquote><p>Quoted paragraph</p>"));
        Assert.That(html, Does.Contain("<a href=\"https://blog.example.com/post\">Read the original</a>"));
        Assert.That(html, Does.Contain("unknown tag text"));
    }

    [Test]
    public void LocalImage_WithoutPayloadUrl_RendersEmptyFigure()
    {
        var html = PlateRichTextParser.Parse("""[{"type":"local_image","fileKey":"k","children":[{"text":""}]}]""");
        Assert.That(html, Is.EqualTo("<figure></figure>\n"));
    }

    [Test]
    public void ObjectBody_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => PlateRichTextParser.Parse("""{"type":"p","children":[{"text":"hi"}]}"""));
        Assert.That(PlateRichTextParser.Parse("""{"foo":1}"""), Is.EqualTo(""));
        Assert.That(PlateRichTextParser.Parse("""[null, 5, "s", {"type":5}]"""), Is.EqualTo(""));
        Assert.That(PlateRichTextParser.Parse("not json"), Is.Null);
    }

    [Test]
    public void UnknownNode_RendersChildren()
    {
        var html = PlateRichTextParser.Parse("""[{"type":"mystery","children":[{"text":"inner","bold":true}]}]""");
        Assert.That(html, Is.EqualTo("<strong>inner</strong>"));
    }

    [Test]
    public void TextIsEncodedAndHrefsRestricted()
    {
        var html = PlateRichTextParser.Parse("""
            [{"type":"p","children":[
              {"text":"<script>alert(1)</script>"},
              {"type":"a","url":"javascript:alert(1)","children":[{"text":"bad"}]},
              {"type":"a","url":"mailto:a@b.co","children":[{"text":"mail"}]},
              {"type":"a","url":"https://a.test/?q=\"x\"","children":[{"text":"ok"}]},
              {"type":"mention","value":"x\"><script>","children":[{"text":""}]}
            ]}]
            """)!;

        Assert.That(html, Does.Not.Contain("<script>"));
        Assert.That(html, Does.Not.Contain("javascript:"));
        Assert.That(html, Does.Contain("&lt;script&gt;"));
        Assert.That(html, Does.Contain("<a href=\"mailto:a@b.co\">mail</a>"));
        Assert.That(html, Does.Contain("href=\"https://a.test/?q=&quot;x&quot;\""));
        Assert.That(html, Does.Contain("bad"));
    }
}
