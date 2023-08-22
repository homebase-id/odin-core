using System;
using Microsoft.Extensions.Caching.Memory;

namespace Odin.Core.Services.Authentication.YouAuth;

public class YouAuthSharedSecrets
{
    private readonly IMemoryCache _sharedSecrets = new MemoryCache(new MemoryCacheOptions());

    public bool TryGetSecret(string key, out SensitiveByteArray secret)
    {
        return _sharedSecrets.TryGetValue(key, out secret);
    }

    public void SetSecret(string key, SensitiveByteArray secret)
    {
        _sharedSecrets.Set(key, secret, TimeSpan.FromMinutes(5));
    }
}