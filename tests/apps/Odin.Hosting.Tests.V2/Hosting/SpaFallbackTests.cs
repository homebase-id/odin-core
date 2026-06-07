#nullable enable
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Odin.Hosting;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Unit tests for <see cref="SpaFallback"/> — the shared deep-link fallback guard used by every
/// statically-served front-end app (owner, feed, chat, mail, community, chat-wasm). These assert
/// the rule that matters regardless of which app it is: a navigation gets the SPA shell, but an
/// asset request gets a clean 404 instead of being masked as <c>200 index.html</c> (the masking
/// that hid the chat-wasm blank-text bug, where Compose received index.html for its
/// <c>strings.commonMain.cvr</c> string table). No host boot — pure logic, microseconds.
/// </summary>
[TestFixture]
public class SpaFallbackTests
{
    // Browser navigation Accept headers — these must resolve to the SPA shell.
    [TestCase("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8")]
    [TestCase("text/html")]
    [TestCase("TEXT/HTML")] // case-insensitive
    public void WantsHtmlDocument_TrueForNavigationAccept(string accept)
    {
        Assert.That(SpaFallback.WantsHtmlDocument(RequestWithAccept(accept)), Is.True);
    }

    // Asset loads (fetch/XHR/script/style/img) and absent Accept — these must NOT get the shell.
    [TestCase("*/*")]                       // fetch() / XHR default
    [TestCase("image/avif,image/webp,*/*")] // <img>
    [TestCase("text/css,*/*;q=0.1")]        // <link rel=stylesheet>
    [TestCase("application/octet-stream")]  // a Compose .cvr fetch lands here
    [TestCase("")]                          // no Accept header at all
    public void WantsHtmlDocument_FalseForAssetAccept(string accept)
    {
        Assert.That(SpaFallback.WantsHtmlDocument(RequestWithAccept(accept)), Is.False);
    }

    [Test]
    public async Task ServeShellOrNotFound_AssetRequest_Returns404AndDoesNotTouchFile()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Accept = "*/*";

        // A path that does not exist — the guard must short-circuit to 404 BEFORE any file access,
        // so a missing/misconfigured asset surfaces as a missing asset rather than the SPA shell.
        await SpaFallback.ServeShellOrNotFound(ctx, "/does/not/exist/index.html");

        Assert.That(ctx.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.NotFound));
        Assert.That(ctx.Response.Headers.ContentType.ToString(), Does.Not.Contain("text/html"));
    }

    [Test]
    public async Task ServeShellOrNotFound_Navigation_ServesTheShell()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"spa-fallback-test-{System.Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(indexPath, "<!doctype html><title>shell</title>");
        try
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers.Accept = "text/html";
            ctx.Response.Body = new MemoryStream();

            await SpaFallback.ServeShellOrNotFound(ctx, indexPath);

            Assert.That(ctx.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.OK));
            Assert.That(ctx.Response.Headers.ContentType.ToString(), Does.Contain("text/html"));
        }
        finally
        {
            File.Delete(indexPath);
        }
    }

    private static HttpRequest RequestWithAccept(string accept)
    {
        var ctx = new DefaultHttpContext();
        if (accept.Length > 0)
        {
            ctx.Request.Headers.Accept = accept;
        }

        return ctx.Request;
    }
}
