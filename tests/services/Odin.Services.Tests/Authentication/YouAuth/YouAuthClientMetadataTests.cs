#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Http;
using Odin.Core.Storage.Cache;
using Odin.Core.Util;
using Odin.Services.Authentication.YouAuth;

namespace Odin.Services.Tests.Authentication.YouAuth;

/// <summary>
/// What an identity accepts from a relying party's <c>/.well-known/youauth-client.json</c>, and
/// nothing more: a field survives only if it belongs to the redirect domain the document came from.
/// See docs/youauth-client-metadata-plan.md.
/// </summary>
[TestFixture]
public class YouAuthClientMetadataTests
{
    private static readonly AsciiDomainName Amazoom = new("amazoom.org");

    // ---------------------------------------------------------------------------------------
    // Parsing: the document is the domain's word about itself, and only about itself
    // ---------------------------------------------------------------------------------------

    [Test]
    public void AValidDocumentRoundTrips()
    {
        var doc = YouAuthClientMetadata.Parse(Amazoom,
            """{"name":"Amazoom","logo":"https://amazoom.org/logo.png","redirect_uris":["https://amazoom.org/auth/callback"]}""");

        Assert.That(doc.Name, Is.EqualTo("Amazoom"));
        Assert.That(doc.Logo, Is.EqualTo("https://amazoom.org/logo.png"));
        Assert.That(doc.RedirectUris, Is.EqualTo(new[] { "https://amazoom.org/auth/callback" }));
    }

    [Test]
    public void UnparsableJsonIsEmpty()
    {
        Assert.That(YouAuthClientMetadata.Parse(Amazoom, "this is not json").IsEmpty, Is.True);
        Assert.That(YouAuthClientMetadata.Parse(Amazoom, "").IsEmpty, Is.True);
        Assert.That(YouAuthClientMetadata.Parse(Amazoom, "[1,2,3]").IsEmpty, Is.True);
    }

    [Test]
    public void ARedirectUriOnAnotherHostIsDroppedAndTheRestSurvives()
    {
        var doc = YouAuthClientMetadata.Parse(Amazoom,
            """{"name":"Amazoom","redirect_uris":["https://evil.example/callback","https://amazoom.org/ok","http://amazoom.org/plain","not a url"]}""");

        Assert.That(doc.Name, Is.EqualTo("Amazoom"), "one bad entry does not discard the document");
        Assert.That(doc.RedirectUris, Is.EqualTo(new[] { "https://amazoom.org/ok" }),
            "only https callbacks on the redirect domain are the domain's to claim");
    }

    [Test]
    public void ALogoOnAnotherHostIsDropped()
    {
        var doc = YouAuthClientMetadata.Parse(Amazoom, """{"logo":"https://cdn.example/logo.png"}""");
        Assert.That(doc.Logo, Is.Null);

        var plain = YouAuthClientMetadata.Parse(Amazoom, """{"logo":"http://amazoom.org/logo.png"}""");
        Assert.That(plain.Logo, Is.Null, "https only");
    }

    [Test]
    public void TheNameIsCleanedAndCapped()
    {
        var doc = YouAuthClientMetadata.Parse(Amazoom, "{\"name\":\"  Ama\\u0000zoom\\n\\n   Store \"}");
        Assert.That(doc.Name, Is.EqualTo("Amazoom Store"), "control characters go, whitespace collapses, ends trim");

        var longName = new string('a', 500);
        var capped = YouAuthClientMetadata.Parse(Amazoom, $"{{\"name\":\"{longName}\"}}");
        Assert.That(capped.Name!.Length, Is.EqualTo(64), "a name is a label, not a page");

        var blank = YouAuthClientMetadata.Parse(Amazoom, "{\"name\":\"   \"}");
        Assert.That(blank.Name, Is.Null, "a blank name is no name");
    }

    [Test]
    public void AnEmptyRedirectListIsNoList()
    {
        var doc = YouAuthClientMetadata.Parse(Amazoom, """{"redirect_uris":[]}""");
        Assert.That(doc.RedirectUris, Is.Null, "publishing no callbacks must not lock every callback out");
        Assert.That(doc.AllowsRedirect(new Uri("https://amazoom.org/anything")), Is.True);
    }

    [Test]
    public void AllowsRedirectMatchesSchemeHostAndPathAndIgnoresTheQuery()
    {
        var doc = YouAuthClientMetadata.Parse(Amazoom, """{"redirect_uris":["https://amazoom.org/auth/callback"]}""");

        Assert.That(doc.AllowsRedirect(new Uri("https://amazoom.org/auth/callback")), Is.True);
        Assert.That(doc.AllowsRedirect(new Uri("https://amazoom.org/auth/callback?session=abc")), Is.True,
            "the relying party's own query is kept on the way back, so it is not part of the pin");
        Assert.That(doc.AllowsRedirect(new Uri("https://amazoom.org/auth/callback/extra")), Is.False);
        Assert.That(doc.AllowsRedirect(new Uri("https://amazoom.org/other")), Is.False);
        Assert.That(doc.AllowsRedirect(new Uri("http://amazoom.org/auth/callback")), Is.False);
    }

    // ---------------------------------------------------------------------------------------
    // Fetching: a site that is down, absent, or talking nonsense is a site with no document
    // ---------------------------------------------------------------------------------------

    [Test]
    public async Task AnAbsentDocumentIsEmpty()
    {
        var fetcher = FetcherAnswering(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var doc = await fetcher.FetchAsync(Amazoom);
        Assert.That(doc.IsEmpty, Is.True);
    }

    [Test]
    public async Task TheDocumentIsFetchedFromTheWellKnownPathOnTheClientsOwnHost()
    {
        Uri? requested = null;
        var fetcher = FetcherAnswering(request =>
        {
            requested = request.RequestUri;
            return Json("""{"name":"Amazoom"}""");
        });

        var doc = await fetcher.FetchAsync(Amazoom);

        Assert.That(requested, Is.EqualTo(new Uri("https://amazoom.org/.well-known/youauth-client.json")));
        Assert.That(doc.Name, Is.EqualTo("Amazoom"));
    }

    [Test]
    public async Task AWrongContentTypeIsEmpty()
    {
        var fetcher = FetcherAnswering(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"name":"Amazoom"}""", Encoding.UTF8, "text/html")
        });
        var doc = await fetcher.FetchAsync(Amazoom);
        Assert.That(doc.IsEmpty, Is.True, "an HTML page at the well-known path is not a document");
    }

    [Test]
    public async Task ABodyOverTheSizeCapIsEmpty()
    {
        var padding = new string('x', 20_000);
        var fetcher = FetcherAnswering(_ => Json($"{{\"name\":\"Amazoom\",\"padding\":\"{padding}\"}}"));
        var doc = await fetcher.FetchAsync(Amazoom);
        Assert.That(doc.IsEmpty, Is.True, "a document is a few hundred bytes; anything larger is not read");
    }

    [Test]
    public async Task AFailingSiteIsEmptyNotAnException()
    {
        var fetcher = FetcherAnswering(_ => throw new HttpRequestException("connection refused"));
        var doc = await fetcher.FetchAsync(Amazoom);
        Assert.That(doc.IsEmpty, Is.True, "a site that is down must not stall or fail a login");
    }

    // ---------------------------------------------------------------------------------------
    // Caching: every login reads the document, not every login reads the site
    // ---------------------------------------------------------------------------------------

    [Test]
    public async Task TheDocumentIsFetchedOnceAndServedFromCache()
    {
        await using var container = BuildContainerWithCaches();
        var fetcher = new Mock<IYouAuthClientMetadataFetcher>(MockBehavior.Strict);
        fetcher.Setup(f => f.FetchAsync(Amazoom, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YouAuthClientMetadata { Name = "Amazoom" });

        var service = new YouAuthClientMetadataService(fetcher.Object,
            container.Resolve<ITenantLevel2Cache<YouAuthClientMetadataService>>());

        var first = await service.GetAsync(Amazoom);
        var second = await service.GetAsync(Amazoom);

        Assert.That(first.Name, Is.EqualTo("Amazoom"));
        Assert.That(second.Name, Is.EqualTo("Amazoom"));
        fetcher.Verify(f => f.FetchAsync(Amazoom, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AnAbsentDocumentIsCachedToo()
    {
        await using var container = BuildContainerWithCaches();
        var fetcher = new Mock<IYouAuthClientMetadataFetcher>(MockBehavior.Strict);
        fetcher.Setup(f => f.FetchAsync(Amazoom, It.IsAny<CancellationToken>()))
            .ReturnsAsync(YouAuthClientMetadata.Empty);

        var service = new YouAuthClientMetadataService(fetcher.Object,
            container.Resolve<ITenantLevel2Cache<YouAuthClientMetadataService>>());

        await service.GetAsync(Amazoom);
        await service.GetAsync(Amazoom);

        fetcher.Verify(f => f.FetchAsync(Amazoom, It.IsAny<CancellationToken>()), Times.Once,
            "a site with no document must not be asked again on every login");
    }

    [Test]
    public async Task AnAddressIsNeverFetched()
    {
        // "127.0.0.1" and "10.0.0.5" pass the domain validator (labels of digits), so the client id
        // can be an address. Nothing legitimate publishes a document at one, and a fetch there would
        // be this server making a request into whatever network it sits on.
        await using var container = BuildContainerWithCaches();
        var fetcher = new Mock<IYouAuthClientMetadataFetcher>(MockBehavior.Strict);

        var service = new YouAuthClientMetadataService(fetcher.Object,
            container.Resolve<ITenantLevel2Cache<YouAuthClientMetadataService>>());

        Assert.That((await service.GetAsync(new AsciiDomainName("127.0.0.1"))).IsEmpty, Is.True);
        Assert.That((await service.GetAsync(new AsciiDomainName("10.0.0.5"))).IsEmpty, Is.True);
        fetcher.VerifyNoOtherCalls();
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static YouAuthClientMetadataFetcher FetcherAnswering(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new Mock<IDynamicHttpClientFactory>(MockBehavior.Loose);
        factory
            .Setup(x => x.CreateClient(It.IsAny<string>(), It.IsAny<Action<ClientHandlerConfig>?>()))
            .Returns(() => new HttpClient(new DispatchingHandler(responder)));

        return new YouAuthClientMetadataFetcher(factory.Object, new Mock<ILogger<YouAuthClientMetadataFetcher>>().Object);
    }

    private static IContainer BuildContainerWithCaches()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreCacheServices(new CacheConfiguration
        {
            MemoryCacheSizeLimit = long.MaxValue,
            MemoryCacheCompactionPercentage = 0.25,
            Level2CacheType = Level2CacheType.None,
        });

        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.AddSystemCaches();
        builder.AddTenantCaches("frodo.me");
        return builder.Build();
    }

    private class DispatchingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
