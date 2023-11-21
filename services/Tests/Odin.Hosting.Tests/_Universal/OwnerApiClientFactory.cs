using System;
using System.Net.Http;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base;
using Odin.Core.Storage;
using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests._Universal;

public class OwnerApiClientFactory : IApiClientFactory
{
    private readonly OwnerApiTestUtils _oldOwnerApi;

    public OwnerApiClientFactory(OwnerApiTestUtils oldOwnerApi)
    {
        _oldOwnerApi = oldOwnerApi;
    }

    public HttpClient CreateHttpClient(OdinId identity, out SensitiveByteArray sharedSecret, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = WebScaffold.CreateHttpClient<OwnerApiTestUtils>();

        var t = _oldOwnerApi.GetOwnerAuthContext(identity).ConfigureAwait(false).GetAwaiter().GetResult();

        var token = t.AuthenticationResult;
        sharedSecret = t.SharedSecret;
        
        //
        // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
        // DO NOT do this in production code!
        //
        {
            var cookieValue = $"{OwnerAuthConstants.CookieName}={token}";
            client.DefaultRequestHeaders.Add("Cookie", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(sharedSecret.GetKey()));
        }

        client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(typeof(FileSystemType), fileSystemType));
        client.Timeout = TimeSpan.FromMinutes(15);

        client.BaseAddress = new Uri($"https://{identity}{OwnerApiPathConstants.BasePathV1}");
        return client;
    }
}