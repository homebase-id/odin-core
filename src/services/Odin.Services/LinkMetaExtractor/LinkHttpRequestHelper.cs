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
                var mimeType = response.Content.Headers.ContentType?.ToString();
                if (string.IsNullOrEmpty(mimeType))
                    mimeType = "image/png";
                var imageBase64 = Convert.ToBase64String(image);
                if (string.IsNullOrWhiteSpace(imageBase64))
                {
                    logger.LogDebug("LinkExtractor: No image Data from imageUrl {Url}. Original Link URL {OriginalUrl}", request.RequestUri, originalUrl);
                    return null;
                }
                var imageUri = $"data:{mimeType};base64,{imageBase64}";
                return imageUri;
            }
            catch (Exception e)
            {
                logger.LogInformation("LinkExtractor: Something went seriously wrong that you are here {Url}. Error: {Error}", request.RequestUri, e.Message);
                return null;
            }
        }




    }
}
