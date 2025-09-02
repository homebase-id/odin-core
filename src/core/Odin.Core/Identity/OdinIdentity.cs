using System;
using System.Diagnostics;

namespace Odin.Core.Identity;

[DebuggerDisplay("{PrimaryDomain} - {IdentityId}")]
public class OdinIdentity(Guid identityId, string primaryDomain)
{
    public Guid IdentityId { get; } = identityId;
    public string PrimaryDomain { get; } = primaryDomain;

    public static implicit operator Guid(OdinIdentity odinIdentity)
    {
        return odinIdentity.IdentityId;
    }

    public static implicit operator string(OdinIdentity odinIdentity)
    {
        return odinIdentity.PrimaryDomain;
    }

    public byte[] IdentityIdAsByteArray()
    {
        return IdentityId.ToByteArray();
    }
}
