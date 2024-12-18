using System.Threading.Tasks;
using AngleSharp.Css.Values;
using Autofac.Features.Metadata;
using System.Xml.Linq;
using HttpClientFactoryLite;
using NUnit.Framework;
using NUnit.Framework.Internal;
using Odin.Core.Exceptions;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Services.LinkMetaExtractor;
using Odin.Test.Helpers.Logging;

namespace Odin.Services.Tests.LinkMetaExtractor;

public class LinkMetaExtractorTests
{
    private readonly HttpClientFactory _httpClientFactory = new ();

#if !CI_GITHUB
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

#if !CI_GITHUB
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
    
#if !CI_GITHUB
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

#if !CI_GITHUB
    // !CI_GITHUB because it sometimes instagram blocks the request and does not send a static website
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
    public void BasicFunctionality()
    {
        // Arrange

        var html = 
            """
            <html>
                <head>
                    <title>Test Pæge</title>
                    <meta name="description" content="This is a test pøge." />
                    <meta property="og:title" content="OG Tøtle" />
                </head>
                <body></body>
            </html>
            """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        // Verify that script tags or JavaScript content have been removed
        Assert.AreEqual("Test Pæge", sanitizedMetadata["title"]);
        Assert.AreEqual("This is a test pøge.", sanitizedMetadata["description"]);
        Assert.AreEqual("OG Tøtle", sanitizedMetadata["og:title"]);
    }



    [Test]
    public void TestHtmlSanitation_RemovesJavaScript()
    {
        // Arrange
        var html =
            """
            <html>
                <head>
                    <title>Test</title>
                    <meta name="description" content="<script>alert('test')</script> This is a description." />
                    <meta property="og:title" content="Valid OG Title" />
                    <meta name="viewport" content="width=device-width, initial-scale=1" />
                </head>
                <body>
                    <script>alert('danger');</script>
                    <h1>This is the body content</h1>
                </body>
            </html>
            """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        // Verify that script tags or JavaScript content have been removed
        Assert.AreEqual("Test", sanitizedMetadata["title"]);
        Assert.AreEqual("This is a description.", sanitizedMetadata["description"]);
        Assert.AreEqual("Valid OG Title", sanitizedMetadata["og:title"]);

        // Ensure no script content remains anywhere in the sanitized metadata
        foreach (var value in sanitizedMetadata.Values)
        {
            if (value is string stringValue)
            {
                Assert.IsFalse(stringValue.Contains("<script>") || stringValue.Contains("alert("),
                    $"Sanitized output contains unsafe content: {stringValue}");
            }
        }
    }


    [Test]
    public void TestHtmlSanitation_HandleSpacingAndCasing()
    {
        // Arrange
        var html =
            """
        <html>
            <head>
                <title>Test</title>
                <meta    name =   "description"   content =   "  <script>alert('test')</script> This is a description.  "   />
                <meta    property = " Og:title "   content =   "   Valid OG Title   "   />
                <meta    name =    "viewport"   content =    " width=device-width, initial-scale=1"   />
            </head>
            <body>
                <script>alert('danger');</script>
                <h1>This is the body content</h1>
            </body>
        </html>
        """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        // Verify that script tags or JavaScript content have been removed
        Assert.AreEqual("Test", sanitizedMetadata["title"]);
        Assert.AreEqual("This is a description.", sanitizedMetadata["description"]);
        Assert.AreEqual("Valid OG Title", sanitizedMetadata["og:title"]);

        // Ensure no script content remains anywhere in the sanitized metadata
        foreach (var value in sanitizedMetadata.Values)
        {
            if (value is string stringValue)
            {
                Assert.IsFalse(stringValue.Contains("<script>") || stringValue.Contains("alert("),
                    $"Sanitized output contains unsafe content: {stringValue}");
            }
        }
    }

#if !CI_GITHUB
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
    #endif
    
}