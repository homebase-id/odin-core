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
using System.Collections.Generic;
using System.Text;

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
    public void BasicFunctionalityUtf8()
    {
        // Arrange

        var html =
            """
            <html>
                <head>
                    <title>Test Pæge</title>
                    <meta name="description" content="你好世界 (Chinese) | こんにちは世界 (Japanese) | ᚠᚢᚦᚨᚱᚴ (Nordic Runes) | Tænk på det (Danish) | Привет мир (Russian)" />
                    <meta property="og:title" content="OG Tøtle" />
                </head>
                <body></body>
            </html>
            """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        // Verify that script tags or JavaScript content have been removed
        Assert.AreEqual("你好世界 (Chinese) | こんにちは世界 (Japanese) | ᚠᚢᚦᚨᚱᚴ (Nordic Runes) | T&#230;nk p&#229; det (Danish) | Привет мир (Russian)", sanitizedMetadata["description"]);
        Assert.AreEqual("Test P&#230;ge", sanitizedMetadata["title"]);
        Assert.AreEqual("OG T&#248;tle", sanitizedMetadata["og:title"]);
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
        Assert.AreEqual("Test P&#230;ge", sanitizedMetadata["title"]);
        Assert.AreEqual("OG T&#248;tle", sanitizedMetadata["og:title"]);
        Assert.AreEqual("This is a test p&#248;ge.", sanitizedMetadata["description"]);
    }


    // Current HTML parser is not very tolerant for malformed HTML
    [Test]
    public void MalformedHtml()
    {
        // Arrange
        var html = """
    <html>
        <head>
            <title>Broken Page<title>
            <meta name="description" content="Unclosed meta
            <meta property="og:title" content="OG Title"
    </html>
    """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        // Adjust expectations for malformed input
        Assert.IsFalse(sanitizedMetadata.ContainsKey("title")); // Malformed title should be ignored
        Assert.IsFalse(sanitizedMetadata.ContainsKey("description")); // Malformed meta should be ignored
        Assert.IsFalse(sanitizedMetadata.ContainsKey("og:title")); // Malformed meta should be ignored
    }


    [Test]
    public void DuplicateMetaTags()
    {
        // Arrange
        var html = """
    <html>
        <head>
            <meta property="og:title" content="OG Title 1" />
            <meta property="og:title" content="OG Title 2" />
        </head>
    </html>
    """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        Assert.IsTrue(sanitizedMetadata["og:title"] is List<string>);
        var titles = (List<string>)sanitizedMetadata["og:title"];
        CollectionAssert.AreEqual(new[] { "OG Title 1", "OG Title 2" }, titles);
    }


    [Test]
    public void NonStandardTags()
    {
        // Arrange
        var html = """
    <html>
        <head>
            <meta name="irrelevant" content="Not important" />
            <meta property="og:title" content="OG Title" />
        </head>
    </html>
    """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        Assert.AreEqual("OG Title", sanitizedMetadata["og:title"]);
        Assert.IsFalse(sanitizedMetadata.ContainsKey("irrelevant"));
    }

    [Test]
    public void SpecialCharactersAndHtmlEntities()
    {
        // Arrange
        var html = """
    <html>
        <head>
            <meta property="og:title" content="Title &amp; Subtitle" />
            <meta name="description" content="This is a test &lt;description&gt;" />
        </head>
    </html>
    """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        Assert.AreEqual("Title &amp; Subtitle", sanitizedMetadata["og:title"]);
        Assert.AreEqual("This is a test &lt;description&gt;", sanitizedMetadata["description"]);
    }
    [Test]


    public void EmptyOrMissingMetadata()
    {
        // Arrange
        var html = """
    <html>
        <head></head>
        <body></body>
    </html>
    """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        Assert.IsFalse(sanitizedMetadata.ContainsKey("title"));
        Assert.IsFalse(sanitizedMetadata.ContainsKey("description"));
        Assert.IsFalse(sanitizedMetadata.ContainsKey("og:title"));
    }


    [Test]
    public void CaseInsensitivity()
    {
        // Arrange
        var html = """
    <html>
        <head>
            <meta ProPErty="OG:title" content="Case Insensitive Title" />
        </head>
    </html>
    """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        Assert.AreEqual("Case Insensitive Title", sanitizedMetadata["og:title"]);
    }


    [Test]
    public void HandlingJavaScriptInjection()
    {
        // Arrange
        var html = """
    <html>
        <head>
            <meta name="description" content="<script>alert('test')</script>Safe Description" />
        </head>
    </html>
    """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        Assert.AreEqual("Safe Description", sanitizedMetadata["description"]);
        Assert.IsFalse(sanitizedMetadata["description"].ToString().Contains("<script>"));
    }
    [Test]
    public void LargeHtmlContent()
    {
        // Arrange
        var html = """
    <html>
        <head>
            <meta property="og:title" content="OG Title" />
            <!-- Repeat similar meta tags -->
    """;

        for (int i = 0; i < 1000; i++)
        {
            html += $"""
        <meta property="og:custom{i}" content="Custom Value {i}" />
        """;
        }

        html += """
        </head>
    </html>
    """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        Assert.AreEqual("OG Title", sanitizedMetadata["og:title"]);
        for (int i = 0; i < 1000; i++)
        {
            Assert.AreEqual($"Custom Value {i}", sanitizedMetadata[$"og:custom{i}"]);
        }
    }
    [Test]
    public void NonEnglishOrUnicodeCharacters()
    {
        // Arrange
        var html = """
    <html>
        <head>
            <meta property="og:title" content="标题" />
            <meta name="description" content="描述内容" />
        </head>
    </html>
    """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        Assert.AreEqual("标题", sanitizedMetadata["og:title"]);
        Assert.AreEqual("描述内容", sanitizedMetadata["description"]);
    }


    [Test]
    public void InvalidAttributes()
    {
        // Arrange
        var html = """
    <html>
        <head>
            <meta property="og:title" />
            <meta name="description" />
        </head>
    </html>
    """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        Assert.IsFalse(sanitizedMetadata.ContainsKey("og:title"));
        Assert.IsFalse(sanitizedMetadata.ContainsKey("description"));
    }


    [Test]
    public void GetTitle_PrioritizesMetaKeysAndSkipsEmptyValues()
    {
        // Arrange
        var metaPermutations = new[]
        {
            new Dictionary<string, object> { { "title", "Title Value" } },
            new Dictionary<string, object> { { "og:title", "OG Title Value" } },
            new Dictionary<string, object> { { "twitter:title", "Twitter Title Value" } },
            new Dictionary<string, object> { { "title", "" }, { "og:title", "OG Title Value" } },
            new Dictionary<string, object> { { "title", "" }, { "og:title", "" }, { "twitter:title", "Twitter Title Value" } },
            new Dictionary<string, object> { { "og:title", "" }, { "twitter:title", "Twitter Title Value" } },
            new Dictionary<string, object> { { "title", "Title Value" }, { "og:title", "OG Title Value" }, { "twitter:title", "Twitter Title Value" } }
        };

        var expectedResults = new[]
        {
            "Title Value",
            "OG Title Value",
            "Twitter Title Value",
            "OG Title Value",
            "Twitter Title Value",
            "Twitter Title Value",
            "Title Value"
        };

        // Act and Assert
        for (int i = 0; i < metaPermutations.Length; i++)
        {
            var meta = metaPermutations[i];
            var expected = expectedResults[i];

            string? result = typeof(LinkMeta)
                .GetMethod("GetTitle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, new object[] { meta }) as string;

            Assert.AreEqual(expected, result, $"Failed for permutation {i + 1}");
        }
    }


    [Test]
    public void GetDescription_AllPermutations()
    {
        // Arrange
        var permutations = new[]
        {
            // Only one key set
            new { Meta = new Dictionary<string, object> { { "description", "Description 1" } }, Expected = "Description 1" },
            new { Meta = new Dictionary<string, object> { { "og:description", "OG Description" } }, Expected = "OG Description" },
            new { Meta = new Dictionary<string, object> { { "twitter:description", "Twitter Description" } }, Expected = "Twitter Description" },
        
            // Multiple keys with priority
            new { Meta = new Dictionary<string, object> { { "description", "Description 1" }, { "og:description", "OG Description" } }, Expected = "Description 1" },
            new { Meta = new Dictionary<string, object> { { "og:description", "OG Description" }, { "twitter:description", "Twitter Description" } }, Expected = "OG Description" },
            new { Meta = new Dictionary<string, object> { { "description", "Description 1" }, { "twitter:description", "Twitter Description" } }, Expected = "Description 1" },
            new { Meta = new Dictionary<string, object> { { "description", "Description 1" }, { "og:description", "OG Description" }, { "twitter:description", "Twitter Description" } }, Expected = "Description 1" },

            // Keys with empty or whitespace values
            new { Meta = new Dictionary<string, object> { { "description", "   " }, { "og:description", "OG Description" } }, Expected = "OG Description" },
            new { Meta = new Dictionary<string, object> { { "description", "" }, { "twitter:description", "Twitter Description" } }, Expected = "Twitter Description" },
            new { Meta = new Dictionary<string, object> { { "description", null }, { "og:description", "OG Description" } }, Expected = "OG Description" },

            // No description keys
            new { Meta = new Dictionary<string, object>(), Expected = (string?)null }
        };

        foreach (var test in permutations)
        {
            // Act
            var result = LinkMeta.GetDescription(test.Meta);

            // Assert
            Assert.AreEqual(test.Expected, result);
        }
    }


    [Test]
    public void DanishCodepageCharacterWindows1252()
    {
        // Register the encoding provider for codepage support
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Arrange
        // Construct the HTML string in UTF-8
        string htmlUtf8 =
            "<html>" +
            "<head>" +
            "<meta charset=\"windows-1252\" />" +
            "<title>Test Tæxt</title>" +
            "<meta name=\"description\" content=\"This is a dæscription.\" />" +
            "<meta property=\"og:title\" content=\"Og Tætle\" />" +
            "</head>" +
            "</html>";

        // Convert the string to bytes using Windows-1252 encoding
        var windows1252Encoding = Encoding.GetEncoding("windows-1252");
        byte[] encodedBytes = windows1252Encoding.GetBytes(htmlUtf8);

        // Decode the bytes back into a string as if they were read in Windows-1252
        string htmlWindows1252 = windows1252Encoding.GetString(encodedBytes);

        // Simulate misinterpretation by converting back to a different encoding (e.g., UTF-8)
        string misinterpretedHtml = Encoding.UTF8.GetString(encodedBytes);

        // Act
        var sanitizedMetadataCorrect = Parser.Parse(htmlWindows1252);
        var sanitizedMetadataIncorrect = Parser.Parse(misinterpretedHtml);

        // Assert for correct interpretation
        Assert.AreEqual("Test T&#230;xt", sanitizedMetadataCorrect["title"]);
        Assert.AreEqual("This is a d&#230;scription.", sanitizedMetadataCorrect["description"]);
        Assert.AreEqual("Og T&#230;tle", sanitizedMetadataCorrect["og:title"]);

        // Assert for incorrect interpretation (ensure it fails or outputs incorrect results)
        Assert.AreNotEqual("Test T&#230;xt", sanitizedMetadataIncorrect["title"]);
        Assert.AreNotEqual("This is a d&#230;scription.", sanitizedMetadataIncorrect["description"]);
        Assert.AreNotEqual("Og T&#230;tle", sanitizedMetadataIncorrect["og:title"]);
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