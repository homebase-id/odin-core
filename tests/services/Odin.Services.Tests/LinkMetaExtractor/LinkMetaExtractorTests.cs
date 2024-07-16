using System;
using System.Net.Http;
using System.Threading.Tasks;
using HttpClientFactoryLite;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.LinkMetaExtractor;

namespace Odin.Services.Tests.LinkMetaExtractor;

public class LinkMetaExtractorTests
{
    private readonly HttpClientFactory _httpClientFactory = new ();
    private readonly ILogger<Services.LinkMetaExtractor.LinkMetaExtractor> _logger = new Logger<Services.LinkMetaExtractor.LinkMetaExtractor>(new LoggerFactory());
        [Test]
        public async Task TestGithubUrl()
        {
            var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, _logger);
            var ogp = await linkMetaExtractor.ExtractAsync("https://github.com/janhq/jan");
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Description);
            Assert.NotNull(ogp.ImageUrl);
            Assert.NotNull(ogp.ImageWidth);
            Assert.NotNull(ogp.ImageHeight);
            Assert.NotNull(ogp.Type);
            Assert.NotNull(ogp.Url);
        }

        [Test]
        public async Task TestTwitterUrl()
        {
            var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, _logger);

            // Twitter does not return og tags when a http client fetches the page. We need a headless browser to download the webpage and parse the tags
            var ogp =  await linkMetaExtractor.ExtractAsync("https://x.com/trunkio/status/1795913092204998997");
            
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Url);
        }

        [Test]
        public async Task TestYoutubeUrl()
        {
            var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, _logger);
            var ogp = await   linkMetaExtractor.ExtractAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Description);
            Assert.NotNull(ogp.ImageUrl);
        }

        [Test]
        public async Task TestLinkedInUrl()
        {
            var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, _logger);
            var ogp = await  linkMetaExtractor.ExtractAsync("https://www.linkedin.com/posts/flutterdevofficial_calling-all-ai-innovators-join-the-gemini-activity-7201613262163984386-MkaU");
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Description);
            Assert.NotNull(ogp.ImageUrl);
        }

        // Explicit test because it sometimes instagram blocks the request and does not send a static website
        // The main cause are user-agent headers but sometimes it does not send a SSR page
        [Test, Explicit]
        public async Task TestInstagramUrl()
        {
            var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, _logger);
            var ogp = await linkMetaExtractor.ExtractAsync("https://www.instagram.com/reel/C7fhXWKJNeU/");
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Description);
            Assert.NotNull(ogp.ImageUrl);

          
        }

        [Test]
        public async Task TestHtmlUrl()
        {
            var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, _logger);
            var ogp = await  linkMetaExtractor.ExtractAsync("https://simonwillison.net/2024/May/29/training-not-chatting/");
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Description);
            Assert.NotNull(ogp.Url);
        }

        [Test]
        public void TestError()
        {
            var linkMetaExtractor = new Services.LinkMetaExtractor.LinkMetaExtractor(_httpClientFactory, _logger);
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
        
    
    
}