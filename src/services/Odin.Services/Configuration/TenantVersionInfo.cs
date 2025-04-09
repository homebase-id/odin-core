using Odin.Core.Time;
using System;

namespace Odin.Services.Configuration;

public class TenantVersionInfo
{
    public static readonly Guid Key = Guid.Parse("ec6039fe-7a88-4619-b290-78f8a97e2848");
    
    /// <summary>
    /// The version number of the data structure for this tenant
    /// </summary>
    public int DataVersionNumber { get; set; }
    
    public UnixTimeUtc LastUpgraded { get; set; }
}

public class FailedUpgradeVersionInfo
{
    public static readonly Guid Key = Guid.Parse("13535128-b8c4-4530-b215-6a8ec9864ae9");
    
    /// <summary>
    /// The version number of the data structure for this tenant
    /// </summary>
    public int FailedDataVersionNumber { get; set; }
    
    public UnixTimeUtc LastAttempted { get; set; }
    
    /// <summary>
    /// The version on which the upgrade failed 
    /// </summary>
    public string BuildVersion { get; set; }
    
}