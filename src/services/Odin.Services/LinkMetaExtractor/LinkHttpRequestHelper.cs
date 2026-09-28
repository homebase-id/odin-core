using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Odin.Services.LinkMetaExtractor
{
    internal static class LinkHttpRequestHelper
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        /// <returns>null on failure, HttpResponseMessage otherwise (remember to dispose it!)</returns>
        /// <exception cref="OdinClientException"></exception>
        private static async Task<HttpResponseMessage> HttpRequestResponse(HttpRequestMessage request, IDynamicHttpClientFactory clientFactory, ILogger<LinkMetaExtractor> logger, long maxContentLength)
        {
            try
            {
                var response = await clientFactory.SendWithRedirectsAsync(
                    request,
                    timeout: TimeSpan.FromSeconds(20),
                    httpCompletionOption: HttpCompletionOption.ResponseHeadersRead);

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    logger.LogDebug("LinkExtractor: Forbidden to fetch information from {Url}. Status code: {StatusCode}", request.RequestUri, response.StatusCode);
                    return null;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    logger.LogDebug("LinkExtractor: Not OK {Url}. Status code: {StatusCode}", request.RequestUri, response.StatusCode);
                    return null;
                }

                // Check content length
                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > maxContentLength)
                {
                    logger.LogDebug("LinkExtractor: Content length {ContentLength} exceeds maximum allowed size {MaxSize} for url {Url}",
                        contentLength.Value, maxContentLength, request.RequestUri);
                    return null;
                }

                return response;
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled
                logger.LogDebug("LinkExtractor: Request to {Url} timed out", request.RequestUri);
                return null;
            }
            catch (HttpRequestException e)
            {
                logger.LogInformation("LinkExtractor: Error fetching information from {Url}. Error: {Error} StatusCode: {Status}", request.RequestUri,
                    e.Message, e.StatusCode);
                throw new OdinClientException("LinkExtractor: Failed to fetch information from the URL");
            }
            catch (Exception e)
            {
                logger.LogInformation("LinkExtractor: Something went seriously wrong that you are here {Url}. Error: {Error}", request.RequestUri, e.Message);
                return null;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        /// <returns>null on failure, string otherwise</returns>
        /// <exception cref="OdinClientException"></exception>
        public static async Task<string> HttpRequestStringAsync(HttpRequestMessage request,
            IDynamicHttpClientFactory clientFactory, ILogger<LinkMetaExtractor> logger, long maxContentLength)
        {
            try
            {
                using var response = await HttpRequestResponse(request, clientFactory, logger, maxContentLength);
                if (response == null)
                    return null;

                // Read the content with a limited buffer
                var content = await response.Content.ReadAsStringAsync();
                if (content.Length > maxContentLength)
                {
                    logger.LogDebug("LinkExtractor: Content length {ContentLength} exceeds maximum allowed size {MaxSize} for url {Url}",
                        content.Length, maxContentLength, request.RequestUri);
                    return null;
                }

                // Do NOT decode the content, this might be JSON or HTML
                return content;
            }
            catch (Exception e)
            {
                logger.LogInformation("LinkExtractor: Something went seriously wrong that you are here {Url}. Error: {Error}", request.RequestUri, e.Message);
                return null;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        /// <returns>null on failure, string otherwise</returns>
        /// <exception cref="OdinClientException"></exception>
        public static async Task<string> HttpRequestImageAsync(HttpRequestMessage request,
            IDynamicHttpClientFactory clientFactory, ILogger<LinkMetaExtractor> logger, long maxImageSize,
            string originalUrl)
        {
            try
            {
                using var response = await HttpRequestResponse(request, clientFactory, logger, maxImageSize);
                if (response == null)
                    return null;

                var image = await response.Content.ReadAsByteArrayAsync();
                if (image.Length > maxImageSize)
                {
                    logger.LogDebug("LinkExtractor: Image size {ContentLength} exceeds maximum allowed size {MaxSize} for url: {Url}", image.Length, maxImageSize, request.RequestUri);
                    return null;
                }
                // Judged by the bytes, not the Content-Type header, which may be missing or wrong (#1754).
                var mimeType = LinkMeta.SniffImageMimeType(image);
                if (mimeType == null)
                {
                    logger.LogDebug("LinkExtractor: imageUrl {Url} did not return a png, jpeg or gif (Content-Type: {ContentType}). " +
                                    "Original Link URL {OriginalUrl}",
                        request.RequestUri, response.Content.Headers.ContentType, originalUrl);
                    return null;
                }

                return $"data:{mimeType};base64,{Convert.ToBase64String(image)}";
            }
            catch (Exception e)
            {
                logger.LogInformation("LinkExtractor: Something went seriously wrong that you are here {Url}. Error: {Error}", request.RequestUri, e.Message);
                return null;
            }
        }




    }
}
