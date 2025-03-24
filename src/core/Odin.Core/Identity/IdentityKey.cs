using System;

namespace Odin.Core.Identity;

public class IdentityKey(Guid id)
{
    public Guid Id { get; } = id;

    public static implicit operator Guid(IdentityKey key)
    {
        return key.Id;
    }

    public byte[] ToByteArray()
    {
        return Id.ToByteArray();
    }
}
