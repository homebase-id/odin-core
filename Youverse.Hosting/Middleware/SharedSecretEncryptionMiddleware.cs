﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;

namespace Youverse.Hosting.Middleware
{
    public class SharedSecretEncryptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SharedSecretEncryptionMiddleware> _logger;

        private readonly List<string> IgnoredPathsForRequests;

        /// <summary>
        /// Paths that should not have their responses encrypted 
        /// </summary>
        private readonly List<string> IgnoredPathsForResponses;
        //

        public SharedSecretEncryptionMiddleware(
            RequestDelegate next,
            ILogger<SharedSecretEncryptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;

            IgnoredPathsForRequests = new List<string>
            {
                "/api/owner/v1/youauth",
                "/api/owner/v1/authentication",
                "/api/owner/v1/transit/outbox/processor",
                "/api/apps/v1/transit/app/process",
                "/api/perimeter", //TODO: temporarily allowing all perimeter traffic not use shared secret
                "/api/owner/v1/drive/files/upload",
                "/api/apps/v1/drive/files/upload"
            };

            //Paths that should not have their responses encrypted with shared secret
            IgnoredPathsForResponses = new List<string>
            {
                "/api/owner/v1/drive/files/payload",
                "/api/apps/v1/drive/files/payload",
                "/api/youauth/v1/drive/files/payload",
                "/api/owner/v1/drive/files/thumb",
                "/api/apps/v1/drive/files/thumb",
                "/api/youauth/v1/drive/files/thumb",
                "/cdn"
            };

            IgnoredPathsForResponses.AddRange(IgnoredPathsForRequests);
        }

        //

        public async Task Invoke(HttpContext context)
        {
            if (ShouldDecryptRequest(context))
            {
                await DecryptRequest(context);
            }

            if (ShouldEncryptResponse(context))
            {
                using (var responseStream = new MemoryStream())
                {
                    //create a separate response stream to collect all of the content being written
                    var originalBody = context.Response.Body;
                    context.Response.Body = responseStream;

                    try
                    {
                        await _next(context);

                        responseStream.Seek(0L, SeekOrigin.Begin);
                        await EncryptResponse(context, originalBody);
                    }
                    catch (Exception e)
                    {
                        context.Response.Body = originalBody;
                        throw;
                    }
                }
            }
            else
            {
                await _next(context);
            }
        }

        private async Task DecryptRequest(HttpContext context)
        {
            var request = context.Request;

            //TODO: need to detect if the request has a payload
            try
            {
                var encryptedRequest = await JsonSerializer.DeserializeAsync<SharedSecretEncryptedPayload>(request.Body, SerializationConfiguration.JsonSerializerOptions, context.RequestAborted);
                if (null == encryptedRequest)
                {
                    throw new SharedSecretException("Failed to deserialize SharedSecretEncryptedRequest, result was null");
                }

                var key = this.GetSharedSecret(context);
                var encryptedBytes = Convert.FromBase64String(encryptedRequest.Data);
                var decryptedBytes = AesCbc.Decrypt(encryptedBytes, ref key, encryptedRequest.Iv);

                //update the body with the decrypted json file so it can be read down stream as expected
                request.Body = new MemoryStream(decryptedBytes);
            }
            catch (JsonException e)
            {
                throw new SharedSecretException("Failed to decrypt shared secret payload.  Ensure you've provided a body of json formatted as SharedSecretEncryptedPayload");
            }
        }

        private async Task EncryptResponse(HttpContext context, Stream originalBody)
        {
            //if a controller tells us no content, write nothing to the stream
            if (context.Response.StatusCode == (int)HttpStatusCode.NoContent)
            {
                context.Response.Body = originalBody;
                return;
            }

            //TODO: Need to encrypt w/o buffering
            var key = this.GetSharedSecret(context);
            var responseBytes = context.Response.Body.ToByteArray();

            var iv = ByteArrayUtil.GetRndByteArray(16);
            var encryptedBytes = AesCbc.Encrypt(responseBytes, ref key, iv);

            //TODO: might be better to just put the IV as the first 16 bytes
            var encryptedPayload = new SharedSecretEncryptedPayload()
            {
                Iv = iv,
                Data = Convert.ToBase64String(encryptedBytes)
            };

            var finalBytes = JsonSerializer.SerializeToUtf8Bytes(encryptedPayload, encryptedPayload.GetType(), SerializationConfiguration.JsonSerializerOptions);

            context.Response.ContentLength = finalBytes.Length;
            await new MemoryStream(finalBytes).CopyToAsync(originalBody);

            // context.Response.Body.Seek(0, SeekOrigin.Begin);
            // await context.Response.Body.CopyToAsync(originalBody);
            context.Response.Body = originalBody;
        }

        private SensitiveByteArray GetSharedSecret(HttpContext context)
        {
            var accessor = context.RequestServices.GetRequiredService<DotYouContextAccessor>();
            var dotYouContext = accessor.GetCurrent();
            var key = dotYouContext.PermissionsContext?.SharedSecretKey;
            return key;
        }

        private bool ShouldDecryptRequest(HttpContext context)
        {
            if (!context.Request.Path.StartsWithSegments("/api") || context.Request.Method.ToUpper() != "POST" || !CallerMustHaveSharedSecret(context))
            {
                return false;
            }

            return !IgnoredPathsForRequests.Any(p => context.Request.Path.StartsWithSegments(p));
        }

        private bool ShouldEncryptResponse(HttpContext context)
        {
            if (!context.Request.Path.StartsWithSegments("/api") || !CallerMustHaveSharedSecret(context))
            {
                return false;
            }

            return !IgnoredPathsForResponses.Any(p => context.Request.Path.StartsWithSegments(p));
        }


        private bool CallerMustHaveSharedSecret(HttpContext context)
        {
            var accessor = context.RequestServices.GetRequiredService<DotYouContextAccessor>();
            var dotYouContext = accessor.GetCurrent();
            return !dotYouContext.Caller.IsAnonymous && dotYouContext.Caller.SecurityLevel != SecurityGroupType.System;
        }
    }
}