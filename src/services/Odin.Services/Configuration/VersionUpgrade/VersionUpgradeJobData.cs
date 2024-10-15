#nullable enable

namespace Odin.Services.Configuration.VersionUpgrade;

public class VersionUpgradeJobData
{
    public string? Tenant { get; init; }

    public byte[]? Iv { get; init; }

    public byte[]? EncryptedOdinContextData { get; init; }
}