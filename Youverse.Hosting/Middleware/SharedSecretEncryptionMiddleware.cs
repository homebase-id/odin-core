using System;
using System.IO;
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
using Youverse.Core.Services.Base;

namespace Youverse.Hosting.Middleware
{
    public class SharedSecretEncryptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SharedSecretEncryptionMiddleware> _logger;

        //

        public SharedSecretEncryptionMiddleware(
            RequestDelegate next,
            ILogger<SharedSecretEncryptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        //

        public async Task Invoke(HttpContext context)
        {
            if (RequestRequiresDecryption(context))
            {
                await DecryptRequest(context);
            }

            if (ResponseRequiresDecryption(context))
            {
                using (var responseStream = new MemoryStream())
                {
                    var originalBody = context.Response.Body;
                    context.Response.Body = responseStream;

                    await _next(context);

                    responseStream.Seek(0L, SeekOrigin.Begin);
                    await EncryptResponse(context, originalBody);
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

            //todo: detect if this is an api endpoint

            var encryptedRequest = await JsonSerializer.DeserializeAsync<SharedSecretEncryptedPayload>(request.Body, SerializationConfiguration.JsonSerializerOptions, context.RequestAborted);

            if (null == encryptedRequest)
            {
                throw new YouverseException("Failed to deserialize SharedSecretEncryptedRequest");
            }

            var key = this.GetSharedSecret(context);
            var encryptedBytes = Convert.FromBase64String(encryptedRequest.Data);
            var decryptedBytes = AesCbc.Decrypt(encryptedBytes, ref key, encryptedRequest.Iv);

            //update the body with the decrypted json file so it can be read down stream as expected
            request.Body = new MemoryStream(decryptedBytes);
        }

        private async Task EncryptResponse(HttpContext context, Stream originalBody)
        {
            //TODO: Need to encrypt w/o buffering

            var key = this.GetSharedSecret(context);
            var responseBytes = context.Response.Body.ToByteArray();

            var iv = ByteArrayUtil.GetRndByteArray(16);
            var encryptedBytes = AesCbc.Encrypt(responseBytes, ref key, iv);

            //wrap in our object
            //TODO: might be better to just put the IV as the first 16 bytes
            var encryptedPayload = new SharedSecretEncryptedPayload()
            {
                Iv = iv,
                Data = Convert.ToBase64String(encryptedBytes)
            };

            await JsonSerializer.SerializeAsync(originalBody, encryptedPayload, encryptedPayload.GetType(), SerializationConfiguration.JsonSerializerOptions, context.RequestAborted);
        }

        private SensitiveByteArray GetSharedSecret(HttpContext context)
        {
            var accessor = context.RequestServices.GetRequiredService<DotYouContextAccessor>();
            var key = accessor.GetCurrent()?.PermissionsContext?.SharedSecretKey ?? Guid.Empty.ToByteArray().ToSensitiveByteArray(); //hack
            return key;
        }

        private bool RequestRequiresDecryption(HttpContext context)
        {
            //TODO: check paths; skip login, etc.
            return true;
        }

        private bool ResponseRequiresDecryption(HttpContext context)
        {
            //TODO: check paths; skip payloads, etc.
            return true;
        }
    }
}