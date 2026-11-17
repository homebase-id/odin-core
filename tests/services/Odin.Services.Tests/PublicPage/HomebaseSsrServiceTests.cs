using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Services.Drives;
using Odin.Services.PublicPage;
using Odin.Services.PublicPage.Posts;

namespace Odin.Services.Tests.PublicPage;

public class HomebaseSsrServiceTests
{
    private static readonly Guid PostFileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string PayloadBase = "https://frodo.test/api/guest/v1/drive/files/payload?alias=a&type=t";

    private static string Article(ChannelPost post)
    {
        var sb = new StringBuilder();
        new HomebaseSsrService(null!, null!, null!, null!, NullLogger<HomebaseSsrService>.Instance)
            .WritePostArticle("public", post, sb);
        return sb.ToString();
    }

    [Test]
    public void PostFieldsAndComments_AreEncoded()
    {
        var post = new ChannelPost
        {
            FileId = PostFileId,
            ImageUrl = "https://x.test/i.jpg?a=1&b='><script>alert(3)</script>",
            Content = new PostContent
            {
                Caption = "</h1><script>alert(1)</script>",
                Abstract = "<img src=x onerror=alert(2)>",
                Body = """[{"type":"p","children":[{"text":"body"}]}]"""
            },
            ReactionSummary = new ReactionSummary
            {
                TotalCommentCount = 1,
                Comments =
                [
                    new CommentPreview
                    {
                        OdinId = "sam.test'><script>alert(4)</script>",
                        Content = "<script>alert(5)</script>",
                        Reactions = [new ReactionContentPreview { ReactionContent = "<b>x</b>", Count = 2 }]
                    }
                ]
            }
        };

        var html = Article(post);

        Assert.That(html, Does.Not.Contain("<script>"));
        Assert.That(html, Does.Not.Contain("<img src=x"));
        Assert.That(html, Does.Not.Contain("<b>x</b>"));
        Assert.That(html, Does.Contain("<h1>&lt;/h1&gt;&lt;script&gt;alert(1)&lt;/script&gt;</h1>"));
        Assert.That(html, Does.Contain("<img src='https://x.test/i.jpg?a=1&amp;b=&#39;&gt;&lt;script&gt;alert(3)&lt;/script&gt;' width='600'/>"));
        Assert.That(html, Does.Contain("<p>&lt;img src=x onerror=alert(2)&gt;</p>"));
        Assert.That(html, Does.Contain("&lt;script&gt;alert(5)&lt;/script&gt;"));
        Assert.That(html, Does.Contain("&lt;b&gt;x&lt;/b&gt; (2)"));
        Assert.That(html, Does.Contain("<p>body</p>"));
    }

    [Test]
    public void NoImageUrl_RendersNoImage()
    {
        var html = Article(new ChannelPost { Content = new PostContent { Caption = "c" } });
        Assert.That(html, Does.Not.Contain("<img"));
    }

    [Test]
    public void PayloadUrl_EscapesTheKey_UsesAGuidFileId_AndNeedsABase()
    {
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var post = new ChannelPost { FileId = PostFileId, PayloadUrlBase = PayloadBase };
        Assert.That(post.PayloadUrl("a b&c"), Is.EqualTo($"{PayloadBase}&fileId={PostFileId}&key=a%20b%26c"));
        Assert.That(post.PayloadUrl("k", other.ToString()), Is.EqualTo($"{PayloadBase}&fileId={other}&key=k"));
        Assert.That(post.PayloadUrl("k", "not-a-guid"), Is.EqualTo($"{PayloadBase}&fileId={PostFileId}&key=k"));
        Assert.That(new ChannelPost { FileId = PostFileId }.PayloadUrl("k"), Is.Null);
    }
}
