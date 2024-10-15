#nullable enable
using Odin.Services.Base;

namespace Odin.Services.Configuration.VersionUpgrade;

public class VersionUpgradeJobData
{
    public string OdinContextData { get; init; }
    public byte[] Iv { get; set; }
}