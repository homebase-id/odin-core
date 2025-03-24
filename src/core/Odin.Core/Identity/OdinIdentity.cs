using System;
using System.Diagnostics;

namespace Odin.Core.Identity;

[DebuggerDisplay("{PrimaryDomain} - {Id}")]
public class OdinIdentity(Guid id, string primaryDomain)
{
    public Guid Id { get; } = id;
    public string PrimaryDomain { get; } = primaryDomain;

    public static implicit operator Guid(OdinIdentity odinIdentity)
    {
        return odinIdentity.Id;
    }

    public static implicit operator string(OdinIdentity odinIdentity)
    {
        return odinIdentity.PrimaryDomain;
    }

    public byte[] IdAsByteArray()
    {
        return Id.ToByteArray();
    }
}
