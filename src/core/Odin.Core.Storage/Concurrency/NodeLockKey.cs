using System;

namespace Odin.Core.Storage.Concurrency;

#nullable enable

public sealed class NodeLockKey
{
    public string Key { get; private init; } = "";

    private NodeLockKey()
    {
    }

    //

    public static implicit operator string(NodeLockKey nodeLockKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeLockKey.Key, nameof(nodeLockKey.Key));
        return nodeLockKey.Key;
    }

    //

    public static NodeLockKey Create(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var nodeLockKey = new NodeLockKey
        {
            Key = key
        };

        return nodeLockKey;
    }

    //

    public static NodeLockKey Create(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts, nameof(parts));

        if (parts.Length == 0)
        {
            throw new ArgumentException($"{nameof(parts)} must not be empty.", nameof(parts));
        }

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                throw new ArgumentException($"{nameof(parts)} must not be empty", nameof(parts));
            }
        }

        var nodeLockKey = new NodeLockKey
        {
            Key = string.Join(":", parts)
        };

        return nodeLockKey;
    }
}
