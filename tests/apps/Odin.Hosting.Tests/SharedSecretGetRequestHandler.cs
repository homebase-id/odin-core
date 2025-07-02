using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Org.BouncyCastle.Utilities.Encoders;

namespace Odin.Hosting.Tests;

public class SharedSecretGetRequestHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("If this is called for some reason, copy the code from SendAsync");
        // return base.Send(request, cancellationToken);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get)
        {
            string qs = request.RequestUri!.Query;
            if (string.IsNullOrEmpty(qs))
            {
                return base.SendAsync(request, cancellationToken);
            }

            //
            // SEB:NOTE below header values are a hack to make SharedSecretGetRequestHandler work without instance data.
            // DO NOT do this in production code!
            //

            request.Headers.TryGetValues("X-HACK-COOKIE", out var cookieBase64);
            var cookieValue = cookieBase64?.FirstOrDefault();

            if (!request.Headers.TryGetValues("X-HACK-SHARED-SECRET", out var keyBase64))
            {
                throw new Exception("Missing shared secret hack");
            }

            var keyBytes = Base64.Decode(keyBase64.First());
            var key = new SensitiveByteArray(keyBytes);

            var iv = ByteArrayUtil.GetRndByteArray(16);
            var encryptedBytes = AesCbc.Encrypt(qs.ToUtf8ByteArray(), key, iv);

            var payload = new SharedSecretEncryptedPayload()
            {
                Iv = iv,
                Data = encryptedBytes.ToBase64()
            };

            var newQs = $"?ss={HttpUtility.UrlEncode(OdinSystemSerializer.Serialize(payload))}";
            var uri = request.RequestUri;
            var builder = new UriBuilder(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath, newQs);
            var msg = new HttpRequestMessage(request.Method, builder.Uri.ToString());

            //copy over existing headers
            foreach (var header in request.Headers)
            {
                msg.Headers.Add(header.Key, header.Value);
            }
            
            if (!string.IsNullOrEmpty(cookieValue))
            {
                msg.Headers.Add("Cookie", cookieValue);
            }
            
            return base.SendAsync(msg, cancellationToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}