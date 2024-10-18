#nullable enable

using Odin.Core.Identity;

namespace Odin.Services.Membership.Connections.IcrKeyUpgrade;

public class IcrKeyUpgradeJobData
{
    public JobTokenType TokenType { get; init; }
    
    public OdinId? Tenant { get; init; }

    public byte[]? Iv { get; init; }

    public byte[]? EncryptedToken { get; init; }

    public enum JobTokenType
    {
        App = 1,
        Owner = 2
    }
}