using System;

namespace Odin.Core.Storage.Cache;

#nullable enable

public class CacheConfiguration
{
    public Level2CacheType Level2CacheType { get; init; }
    public string? Level2Configuration { get; init; } = "";
}
