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
using System;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.RegularExpressions;
using System.Web;
using System.Net;
using NUnit.Framework.Legacy;

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
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.NotNull(ogp.ImageUrl);
        ClassicAssert.NotNull(ogp.ImageWidth);
        ClassicAssert.NotNull(ogp.ImageHeight);
        ClassicAssert.NotNull(ogp.Type);
        ClassicAssert.NotNull(ogp.Url);
    }
#endif

    [Test]
    public async Task TestEmbeddedImageUrl()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);

        const string htmlContent = @"
        <html>
            <head>
                <meta property='og:title' content='Test Title' />
                <meta property='og:description' content='Test Description' />
                <meta property='og:image' content='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAUA' />
            </head>
        </html>";

        var linkMeta = await linkMetaExtractor.ProcessHtmlAsync(htmlContent, "http://example.com");

        ClassicAssert.NotNull(linkMeta);
        ClassicAssert.AreEqual("Test Title", linkMeta.Title);
        ClassicAssert.AreEqual("Test Description", linkMeta.Description);
        ClassicAssert.NotNull(linkMeta.ImageUrl);
        ClassicAssert.IsTrue(linkMeta.ImageUrl!.StartsWith("data:image/png;base64,"));
    }


    [Test]
    public async Task TestInvalidEmbeddedImage()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);

        const string htmlContent = @"
        <html>
            <head>
                <meta property='og:title' content='Test Title' />
                <meta property='og:description' content='Test Description' />
                <meta property='og:image' content='data:image/invalid;base64,12345' />
            </head>
        </html>";

        var linkMeta = await linkMetaExtractor.ProcessHtmlAsync(htmlContent, "http://example.com");

        ClassicAssert.NotNull(linkMeta);
        ClassicAssert.AreEqual("Test Title", linkMeta.Title);
        ClassicAssert.AreEqual("Test Description", linkMeta.Description);
        ClassicAssert.IsNull(linkMeta.ImageUrl);
    }

#if !CI_GITHUB
    [Test]
    [Explicit]
    public async Task TestTwitterUrl()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);

        // Twitter does not return og tags when a http client fetches the page. We need a headless browser to download the webpage and parse the tags
        var ogp =  await linkMetaExtractor.ExtractAsync("https://x.com/trunkio/status/1795913092204998997");

        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Url);
    }
    #endif

#if !CI_GITHUB
    [Test]
    public async Task TestGoogleMeet()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await linkMetaExtractor.ExtractAsync("https://meet.google.com/kwr-dzwe-kvi?ijlm=1736499961915&hs=187&adhoc=1");
        ClassicAssert.IsNotNull(ogp);
    }
#endif

#if !CI_GITHUB
    [Test]
    public async Task TestInstagramStoryUrl()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await linkMetaExtractor.ExtractAsync("https://www.instagram.com/stories/ashira_oure_bxg_club/3545246080284477588?utm_source=ig_story_item_share&igsh=c2FteGNlYmV1NXZs");
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.NotNull(ogp.ImageUrl);
    }
#endif
    
#if !CI_GITHUB
    [Test]
    public async Task TestFacebookUrl()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await linkMetaExtractor.ExtractAsync("https://www.facebook.com/share/p/14txkE59vN4/");
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.NotNull(ogp.ImageUrl);
    }
#endif
    
    


#if !CI_GITHUB
    [Test]
    public async Task TestYoutubeUrl()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await   linkMetaExtractor.ExtractAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.NotNull(ogp.ImageUrl);
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
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.NotNull(ogp.ImageUrl);
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
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.NotNull(ogp.ImageUrl);
    }
#endif

    [Test]
    public async Task TestHtmlUrl()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await  linkMetaExtractor.ExtractAsync("https://simonwillison.net/2024/May/29/training-not-chatting/");
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.NotNull(ogp.Url);
    }
#if !CI_GITHUB
    [Test]
    public async Task TestCloudFareBlockedURl()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await  linkMetaExtractor.ExtractAsync("https://www.economist.com/schools-brief/2024/07/16/a-short-history-of-ai");
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.NotNull(ogp.Url);
    }
    #endif

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
        ClassicAssert.AreEqual("Test", sanitizedHtml["title"]);
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
        ClassicAssert.AreEqual("你好世界 (Chinese) | こんにちは世界 (Japanese) | ᚠᚢᚦᚨᚱᚴ (Nordic Runes) | Tænk på det (Danish) | Привет мир (Russian)", sanitizedMetadata["description"]);
        ClassicAssert.AreEqual("Test Pæge", sanitizedMetadata["title"]);
        ClassicAssert.AreEqual("OG Tøtle", sanitizedMetadata["og:title"]);
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
        ClassicAssert.AreEqual("Test Pæge", sanitizedMetadata["title"]);
        ClassicAssert.AreEqual("OG Tøtle", sanitizedMetadata["og:title"]);
        ClassicAssert.AreEqual("This is a test pøge.", sanitizedMetadata["description"]);
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
        ClassicAssert.IsFalse(sanitizedMetadata.ContainsKey("title")); // Malformed title should be ignored
        ClassicAssert.IsFalse(sanitizedMetadata.ContainsKey("description")); // Malformed meta should be ignored
        ClassicAssert.IsFalse(sanitizedMetadata.ContainsKey("og:title")); // Malformed meta should be ignored
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
        ClassicAssert.IsTrue(sanitizedMetadata["og:title"] is List<string>);
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
        ClassicAssert.AreEqual("OG Title", sanitizedMetadata["og:title"]);
        ClassicAssert.IsFalse(sanitizedMetadata.ContainsKey("irrelevant"));
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
        ClassicAssert.AreEqual("Title & Subtitle", sanitizedMetadata["og:title"]);
        ClassicAssert.AreEqual("This is a test <description>", sanitizedMetadata["description"]);
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
        ClassicAssert.IsFalse(sanitizedMetadata.ContainsKey("title"));
        ClassicAssert.IsFalse(sanitizedMetadata.ContainsKey("description"));
        ClassicAssert.IsFalse(sanitizedMetadata.ContainsKey("og:title"));
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
        ClassicAssert.AreEqual("Case Insensitive Title", sanitizedMetadata["og:title"]);
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
        ClassicAssert.AreEqual("<script>alert('test')</script>Safe Description", sanitizedMetadata["description"]);
        // ClassicAssert.IsFalse(sanitizedMetadata["description"].ToString()?.Contains("<script>"));
    }


    [Test]
    public void HandlingDecoding()
    {
        // Arrange
        var html = """
                <html>
                    <head>
                        <meta name="description" content="&lt;script&gt;alert('XSS')&lt;/script&gt;&aring;" />
                    </head>
                </html>
                """;

        // Act
        var sanitizedMetadata = Parser.Parse(html);

        // Assert
        ClassicAssert.AreEqual("<script>alert('XSS')</script>å", sanitizedMetadata["description"]);
    }


    [Test]
    public void TestRemovalOfControlCharacters()
    {
        // Arrange: Construct HTML with control characters embedded directly
        var html = $@"
        <html>
            <head>
                <meta name=""description"" content=""Chars: 0x01[{(char)0x01}] 0x1F[{(char)0x1F}] 0x20[{(char)0x20}] 0x7E[{(char)0x7E}] 0x7F[{(char)0x7F}] 0x80[{(char)0x80}] - Done"" />
            </head>
        </html>";

        // Act: Parse and sanitize the HTML
        var sanitizedMetadata = Parser.Parse(html);
        var description = (string) sanitizedMetadata["description"];
        var descriptionDecoded = WebUtility.HtmlDecode(description);

        // Assert: Ensure control characters are removed
        ClassicAssert.IsNotNull(description, "Description metadata should not be null.");
        ClassicAssert.IsFalse(Regex.IsMatch(description.ToString() ?? string.Empty, @"[\x00-\x1F\x7F]"),
            "Control characters, null, and invalid border cases should not be present in the sanitized description.");

        // Assert: Verify expected cleaned output
        ClassicAssert.AreEqual("Chars: 0x01[] 0x1F[] 0x20[ ] 0x7E[~] 0x7F[] 0x80[\u0080] - Done", description);
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
        ClassicAssert.AreEqual("OG Title", sanitizedMetadata["og:title"]);
        for (int i = 0; i < 1000; i++)
        {
            ClassicAssert.AreEqual($"Custom Value {i}", sanitizedMetadata[$"og:custom{i}"]);
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
        ClassicAssert.AreEqual("标题", sanitizedMetadata["og:title"]);
        ClassicAssert.AreEqual("描述内容", sanitizedMetadata["description"]);
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
        ClassicAssert.IsFalse(sanitizedMetadata.ContainsKey("og:title"));
        ClassicAssert.IsFalse(sanitizedMetadata.ContainsKey("description"));
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

            ClassicAssert.AreEqual(expected, result, $"Failed for permutation {i + 1}");
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
            new { Meta = new Dictionary<string, object> { { "description", "" }, { "og:description", "OG Description" } }, Expected = "OG Description" },
            new { Meta = new Dictionary<string, object>(), Expected = (string?)null! }
        };

        foreach (var test in permutations)
        {
            // Act
            var result = LinkMeta.GetDescription(test.Meta);

            // Assert
            ClassicAssert.AreEqual(test.Expected, result);
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
        ClassicAssert.AreEqual("Test Tæxt", sanitizedMetadataCorrect["title"]);
        ClassicAssert.AreEqual("This is a dæscription.", sanitizedMetadataCorrect["description"]);
        ClassicAssert.AreEqual("Og Tætle", sanitizedMetadataCorrect["og:title"]);

        // Assert for incorrect interpretation (ensure it fails or outputs incorrect results)
        ClassicAssert.AreNotEqual("Test Tæxt", sanitizedMetadataIncorrect["title"]);
        ClassicAssert.AreNotEqual("This is a dæscription.", sanitizedMetadataIncorrect["description"]);
        ClassicAssert.AreNotEqual("Og Tætle", sanitizedMetadataIncorrect["og:title"]);
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
        ClassicAssert.AreEqual("Test", sanitizedMetadata["title"]);
        ClassicAssert.AreEqual("<script>alert('test')</script> This is a description.", sanitizedMetadata["description"]);
        ClassicAssert.AreEqual("Valid OG Title", sanitizedMetadata["og:title"]);
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
        ClassicAssert.AreEqual("Test", sanitizedMetadata["title"]);
        ClassicAssert.AreEqual("<script>alert('test')</script> This is a description.", sanitizedMetadata["description"]);
        ClassicAssert.AreEqual("Valid OG Title", sanitizedMetadata["og:title"]);
    }

    [Test]
    public void GetImageUrl_ShouldHandleUrlsAndEmbeddedImages()
    {
        // Arrange
        var maxDataUriSize = 2 * 1024 * 1024; // 2 MB for embedded images
        var validDataUri = "data:image/png;base64," + Convert.ToBase64String(new byte[maxDataUriSize / 2]);
        var invalidDataUri = "data:image/png;base64," + Convert.ToBase64String(new byte[maxDataUriSize * 2]);
        var invalidMimeTypeDataUri = "data:image/xyz;base64,abc123";

        var testCases = new[]
        {
                // Valid URL
                new { Meta = new Dictionary<string, object> { { "og:image", "https://example.com/image.png" } }, Expected = "https://example.com/image.png" },
                new { Meta = new Dictionary<string, object> { { "twitter:image", "https://example.com/image.jpg" } }, Expected = "https://example.com/image.jpg" },

                // Valid embedded image
                new { Meta = new Dictionary<string, object> { { "og:image", validDataUri } }, Expected = validDataUri },
                new { Meta = new Dictionary<string, object> { { "twitter:image", validDataUri } }, Expected = validDataUri },

                // Invalid embedded image (size exceeds limit)
                new { Meta = new Dictionary<string, object> { { "og:image", invalidDataUri } }, Expected = (string?)null! },

                // Invalid MIME type in embedded image
                new { Meta = new Dictionary<string, object> { { "og:image", invalidMimeTypeDataUri } }, Expected = (string?)null! },

                // Invalid URL
                new { Meta = new Dictionary<string, object> { { "og:image", "javascript:alert('XSS')" } }, Expected = (string?)null! },
                new { Meta = new Dictionary<string, object> { { "og:image", "ftp://example.com/image.png" } }, Expected = (string?)null! },

                // No image key
                new { Meta = new Dictionary<string, object>(), Expected = (string?)null! }
            };

        foreach (var testCase in testCases)
        {
            // Act
            var result = LinkMeta.GetImageUrl(testCase.Meta);

            // Assert
            ClassicAssert.AreEqual(testCase.Expected, result, $"Failed for meta: {testCase.Meta}");
        }
    }

#if !CI_GITHUB
    [Test]
    [Explicit]
    public async Task TestBenz()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await linkMetaExtractor.ExtractAsync("https://x.com/i/bookmarks?post_id=1875214258046193880");
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.IsTrue(!ogp.Title.Contains("&amp;") && !ogp.Description!.Contains("&amp;"), "Encoded HTML entities (&amp;) should not be present.");
    }
#endif


#if !CI_GITHUB
    [Explicit]
    [Test]
    public async Task TestAuktionshuset()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await linkMetaExtractor.ExtractAsync("https://www.auktionshuset.dk/auktioner/koretojer-mercedes-benz-vito");
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.IsNull(ogp.ImageUrl); // They have a malformed og:image (relative path not allowed)
    }
#endif


#if !CI_GITHUB
    [Test]
    public async Task TestBrokenImagePreviews()
    {
        var logStore = new LogEventMemoryStore();
        var  logger = TestLogFactory.CreateConsoleLogger<Services.LinkMetaExtractor.LinkMetaExtractor>(logStore);
        var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, logger);
        var ogp = await  linkMetaExtractor.ExtractAsync("https://www.thermal.com/seek-nano.html");
        ClassicAssert.NotNull(ogp.Title);
        ClassicAssert.NotNull(ogp.Description);
        ClassicAssert.Null(ogp.ImageUrl);
    }
    #endif
    
}