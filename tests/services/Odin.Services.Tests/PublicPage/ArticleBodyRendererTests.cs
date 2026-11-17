using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.PublicPage.Posts;

namespace Odin.Services.Tests.PublicPage;

public class ArticleBodyRendererTests
{
    private const string Body = """[{"type":"p","children":[{"text":"slate text"}]}]""";

    private const string TiptapDoc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"tiptap text"}]}]}""";

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static PostContent Content(string? bodyDoc)
    {
        var doc = bodyDoc == null ? "" : ",\"bodyDoc\":" + bodyDoc.Replace("HASH", Hash(Body)).Replace("DOC", TiptapDoc);
        var json = "{\"caption\":\"Trip\",\"type\":\"Article\",\"body\":" + Body + doc + "}";
        return OdinSystemSerializer.Deserialize<PostContent>(json)!;
    }

    [TestCase("""{"format":"tiptap-v1","slateHash":"HASH","doc":DOC}""", "tiptap text")]
    [TestCase("""{"format":"tiptap-v1","slateHash":"stale","doc":DOC}""", "slate text")]
    [TestCase("""{"format":"tiptap-v2","slateHash":"HASH","doc":DOC}""", "slate text")]
    [TestCase(null, "slate text")]
    [TestCase("\"x\"", "slate text")]
    [TestCase("[]", "slate text")]
    [TestCase("5", "slate text")]
    [TestCase("null", "slate text")]
    [TestCase("""{"format":1}""", "slate text")]
    [TestCase("""{"slateHash":5}""", "slate text")]
    [TestCase("""{"format":"tiptap-v1","slateHash":5,"doc":DOC}""", "slate text")]
    [TestCase("""{"format":"tiptap-v1","slateHash":"HASH","doc":"not an object"}""", "slate text")]
    [TestCase("""{"format":"tiptap-v1","slateHash":"HASH","doc":[]}""", "slate text")]
    [TestCase("""{"format":["tiptap-v1"],"slateHash":"HASH","doc":DOC}""", "slate text")]
    [TestCase("""{"format":"tiptap-v1\ud800","slateHash":"HASH\ud800","doc":DOC}""", "slate text")]
    [TestCase("""{"format":"tiptap-v1","slateHash":"HASH","doc":{"type":"doc","type":"x"}}""", "slate text")]
    public void BodyDoc_RendersOnlyWhenItMatchesTheBody_InAnyShape(string? bodyDoc, string expected)
    {
        var content = Content(bodyDoc);

        Assert.That(content.Caption, Is.EqualTo("Trip"));
        var html = ArticleBodyRenderer.Render(Convert.ToString(content.Body)!, content.BodyDoc);
        Assert.That(html, Is.EqualTo($"<p>{expected}</p>\n"));
    }

    [Test]
    public void Description_PrefersTheAbstractThenAMatchingBodyDoc()
    {
        PostContent WithAbstract(string? text, string bodyDoc)
        {
            var content = Content(bodyDoc);
            content.Abstract = text;
            return content;
        }

        const string matching = """{"format":"tiptap-v1","slateHash":"HASH","doc":DOC}""";
        Assert.That(ArticleBodyRenderer.Description(WithAbstract("The abstract", matching)), Is.EqualTo("The abstract"));
        // the abstract stays whole: RSS and JSON-LD carry all of it
        var longAbstract = string.Join(' ', Enumerable.Repeat("word", 80));
        Assert.That(ArticleBodyRenderer.Description(WithAbstract(longAbstract, matching)), Is.EqualTo(longAbstract));
        Assert.That(ArticleBodyRenderer.Description(WithAbstract(null, matching)), Is.EqualTo("tiptap text"));
        Assert.That(ArticleBodyRenderer.Description(WithAbstract(" ", matching)), Is.EqualTo("tiptap text"));
        Assert.That(ArticleBodyRenderer.Description(WithAbstract(null, matching.Replace("HASH", "stale"))), Is.Null);
        Assert.That(ArticleBodyRenderer.Description(null), Is.Null);
    }
}
