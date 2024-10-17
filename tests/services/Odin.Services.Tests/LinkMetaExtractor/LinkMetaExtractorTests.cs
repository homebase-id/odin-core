using System.Threading.Tasks;
using HttpClientFactoryLite;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Services.LinkMetaExtractor;
using Odin.Test.Helpers.Logging;

namespace Odin.Services.Tests.LinkMetaExtractor;

public class LinkMetaExtractorTests
{
    private readonly HttpClientFactory _httpClientFactory = new ();

#if !NOISY_NEIGHBOUR
    [Test]
    public async Task TestGithubUrl()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);

        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await linkMetaExtractor.ExtractAsync("https://github.com/janhq/jan");
        Assert.NotNull(ogp.Title);
        Assert.NotNull(ogp.Description);
        Assert.NotNull(ogp.ImageUrl);
        Assert.NotNull(ogp.ImageWidth);
        Assert.NotNull(ogp.ImageHeight);
        Assert.NotNull(ogp.Type);
        Assert.NotNull(ogp.Url);
    }
#endif

    [Test]
    public async Task TestTwitterUrl()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);

        // Twitter does not return og tags when a http client fetches the page. We need a headless browser to download the webpage and parse the tags
        var ogp =  await linkMetaExtractor.ExtractAsync("https://x.com/trunkio/status/1795913092204998997");

        Assert.NotNull(ogp.Title);
        Assert.NotNull(ogp.Url);
    }

#if !NOISY_NEIGHBOUR
    [Test]
    public async Task TestYoutubeUrl()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await   linkMetaExtractor.ExtractAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        Assert.NotNull(ogp.Title);
        Assert.NotNull(ogp.Description);
        Assert.NotNull(ogp.ImageUrl);
    }
#endif
    
#if !NOISY_NEIGHBOUR
    [Test]
    public async Task TestLinkedInUrl()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await  linkMetaExtractor.ExtractAsync("https://www.linkedin.com/posts/flutterdevofficial_calling-all-ai-innovators-join-the-gemini-activity-7201613262163984386-MkaU");
        Assert.NotNull(ogp.Title);
        Assert.NotNull(ogp.Description);
        Assert.NotNull(ogp.ImageUrl);
    }
#endif

#if !NOISY_NEIGHBOUR
    // !NOISY_NEIGHBOUR because it sometimes instagram blocks the request and does not send a static website
    // The main cause are user-agent headers, but sometimes it does not send an SSR page
    [Test]
    public async Task TestInstagramUrl()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await linkMetaExtractor.ExtractAsync("https://www.instagram.com/reel/C7fhXWKJNeU/");
        Assert.NotNull(ogp.Title);
        Assert.NotNull(ogp.Description);
        Assert.NotNull(ogp.ImageUrl);
    }
#endif

    [Test]
    public async Task TestHtmlUrl()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await  linkMetaExtractor.ExtractAsync("https://simonwillison.net/2024/May/29/training-not-chatting/");
        Assert.NotNull(ogp.Title);
        Assert.NotNull(ogp.Description);
        Assert.NotNull(ogp.Url);
    }

    [Test]
    public async Task TestCloudFareBlockedURl()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await  linkMetaExtractor.ExtractAsync("https://www.economist.com/schools-brief/2024/07/16/a-short-history-of-ai");
        Assert.NotNull(ogp.Title);
        Assert.NotNull(ogp.Description);
        Assert.NotNull(ogp.Url);
    }

    [Test]
    public void TestError()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        Assert.ThrowsAsync<OdinClientException>(async () => await  linkMetaExtractor.ExtractAsync(""));
        Assert.ThrowsAsync<OdinClientException>(async () => await  linkMetaExtractor.ExtractAsync("https://www.go2ogle.com"));
    }

    [Test]
    public void TestHtmlSanitation()
    {
        var html = "<html><head><title>Test</title></head><body><script>alert('test')</script></body></html>";
        var sanitizedHtml = Parser.Parse(html);
        Assert.AreEqual("Test", sanitizedHtml["title"]);
    }
    
    [Test]
    public async Task TestBrokenImagePreviews()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await  linkMetaExtractor.ExtractAsync("https://www.thermal.com/seek-nano.html");
        Assert.NotNull(ogp.Title);
        Assert.NotNull(ogp.Description);
        Assert.Null(ogp.ImageUrl);
    }
    
}