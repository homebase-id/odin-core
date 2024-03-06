using System;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core;

namespace Odin.Services.Authentication.YouAuth;

public class YouAuthSharedSecrets
{
    private readonly IMemoryCache _sharedSecrets = new MemoryCache(new MemoryCacheOptions());

    public bool TryGetSecret(string key, out SensitiveByteArray secret)
    {
        return _sharedSecrets.TryGetValue(key, out secret);
    }

    public bool TryExtractSecret(string key, out SensitiveByteArray secret)
    {
        if (TryGetSecret(key, out secret))
        {
            _sharedSecrets.Remove(key);
            return true;
        }

        return false;
    }

    public void SetSecret(string key, SensitiveByteArray secret)
    {
        _sharedSecrets.Set(key, secret, TimeSpan.FromMinutes(5));
    }
}