using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Youverse.Core;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Serialization;

namespace Youverse.Hosting.Tests;

public class SharedSecretGetRequestHandler : HttpClientHandler
{
    private readonly SensitiveByteArray _sharedSecret;

    public SharedSecretGetRequestHandler(SensitiveByteArray sharedSecret)
    {
        _sharedSecret = sharedSecret;
    }

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

            var iv = ByteArrayUtil.GetRndByteArray(16);
            var key = _sharedSecret; //#wierd
            var encryptedBytes = AesCbc.Encrypt(qs.ToUtf8ByteArray(), ref key, iv);

            var payload = new SharedSecretEncryptedPayload()
            {
                Iv = iv,
                Data = encryptedBytes.ToBase64()
            };

            var newQs = $"?ss={HttpUtility.UrlEncode(DotYouSystemSerializer.Serialize(payload))}";
            var uri = request.RequestUri;
            var builder = new UriBuilder(uri.Scheme, uri.Host, uri.Port,uri.AbsolutePath, newQs);
            var msg = new HttpRequestMessage(request.Method, builder.Uri.ToString());
            return base.SendAsync(msg, cancellationToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}