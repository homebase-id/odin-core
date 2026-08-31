using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;
using Odin.Hosting;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// The SPA shell contract: navigations get the shell WITH a revalidate-always cache policy
/// (a heuristically-cached shell outlives deploys and then 404s its content-hashed assets -
/// the blank-app-after-release bug of 2026-08-31); asset requests get a clean 404.
/// </summary>
[TestFixture]
public class SpaFallbackTests
{
    private string _indexPath = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _indexPath = Path.Combine(Path.GetTempPath(), $"spa-fallback-test-{Guid.NewGuid():N}.html");
        File.WriteAllText(_indexPath, "<!doctype html><title>shell</title>");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        File.Delete(_indexPath);
    }

    private static DefaultHttpContext BrowserContext(string accept)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Accept = accept;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Test]
    public async Task ANavigationGetsTheShellAndMustRevalidateIt()
    {
        var context = BrowserContext("text/html,application/xhtml+xml");

        await SpaFallback.ServeShellOrNotFound(context, _indexPath);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache"),
            "a shell cached on heuristic freshness outlives deploys and 404s its hashed assets");
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.That(body, Does.Contain("shell"));
    }

    [Test]
    public async Task AnAssetRequestGetsACleanNotFound()
    {
        var context = BrowserContext("*/*");

        await SpaFallback.ServeShellOrNotFound(context, _indexPath);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound),
            "a missing asset must surface as missing, never masquerade as the shell");
    }

    [Test]
    public void AnExplicitIndexHtmlRequestMustRevalidateToo()
    {
        var context = new DefaultHttpContext();
        // The static middleware serves it under its real name; only the name matters here.
        SpaFallback.NoCacheIndexHtml(new StaticFileResponseContext(context, new NamedFile("index.html")));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache"));
    }

    [Test]
    public void AHashedAssetKeepsItsCachePolicyUntouched()
    {
        var context = new DefaultHttpContext();
        SpaFallback.NoCacheIndexHtml(new StaticFileResponseContext(context, new NamedFile("homebase-app.41cd37d4.js")));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.Empty,
            "content-hashed assets are immutable per name and may cache freely");
    }

    private sealed class NamedFile(string name) : IFileInfo
    {
        public bool Exists => true;
        public long Length => 0;
        public string PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream();
    }
}
