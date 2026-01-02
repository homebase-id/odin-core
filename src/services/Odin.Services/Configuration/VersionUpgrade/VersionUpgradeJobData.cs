#nullable enable

using Odin.Core.Identity;

namespace Odin.Services.Configuration.VersionUpgrade;

public class VersionUpgradeJobData
{
    public OdinId? Tenant { get; init; }

    public byte[]? Iv { get; init; }

    public byte[]? EncryptedToken { get; init; }
    
    public bool TestMode { get; init; }
}