using System;

namespace Odin.Services.Configuration;

public class TenantVersionInfo
{
    public static readonly Guid Key = Guid.Parse("ec6039fe-7a88-4619-b290-78f8a97e2848");
    
    /// <summary>
    /// The version number of the data structure for this tenant
    /// </summary>
    public int DataVersionNumber { get; set; }
    
    public long LastUpgraded { get; set; }
}