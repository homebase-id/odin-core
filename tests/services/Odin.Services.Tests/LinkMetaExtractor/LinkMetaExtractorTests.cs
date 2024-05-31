using System;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Odin.Services.Tests.LinkMetaExtractor;

public class LinkMetaExtractorTests
{
    private readonly Services.LinkMetaExtractor.LinkMetaExtractor _linkMetaExtractor = new();
    
        [Test]
        public async Task TestGithubUrl()
        {
            var ogp = await _linkMetaExtractor.ExtractAsync("https://github.com/janhq/jan");
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
            // Twitter does not return og tags when a http client fetches the page. We need a headless browser to download the webpage and parse the tags
            var ogp =  await _linkMetaExtractor.ExtractAsync("https://x.com/trunkio/status/1795913092204998997");
            
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Url);
        }

        [Test]
        public async Task TestYoutubeUrl()
        {
            var ogp = await   _linkMetaExtractor.ExtractAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Description);
            Assert.NotNull(ogp.ImageUrl);
        }

        [Test]
        public async Task TestLinkedInUrl()
        {
            var ogp = await  _linkMetaExtractor.ExtractAsync("https://www.linkedin.com/posts/flutterdevofficial_calling-all-ai-innovators-join-the-gemini-activity-7201613262163984386-MkaU");
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Description);
            Assert.NotNull(ogp.ImageUrl);
        }

        [Test]
        public async Task TestInstagramUrl()
        {
            var ogp = await _linkMetaExtractor.ExtractAsync("https://www.instagram.com/reel/C7fhXWKJNeU/");
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Description);
            Assert.NotNull(ogp.ImageUrl);
        }

        [Test]
        public async Task TestHtmlUrl()
        {
            var ogp = await  _linkMetaExtractor.ExtractAsync("https://simonwillison.net/2024/May/29/training-not-chatting/");
            Assert.NotNull(ogp.Title);
            Assert.NotNull(ogp.Description);
            Assert.NotNull(ogp.Url);
        }

        [Test]
        public void TestError()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await  _linkMetaExtractor.ExtractAsync(""));
            Assert.ThrowsAsync<HttpRequestException>(async () => await  _linkMetaExtractor.ExtractAsync("https://www.go2ogle.com"));
        }
        
    
    
}