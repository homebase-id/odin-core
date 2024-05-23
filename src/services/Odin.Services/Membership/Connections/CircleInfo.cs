using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Membership.Connections;

public class CircleInfo
{
    public GuidId CircleDefinitionId { get; set; }
    public string CircleDefinitionName { get; set; }
    public int CircleDefinitionDriveGrantCount { get; set; }

    public CircleAnalysis Analysis { get; set; }
}


public class CircleAnalysis
{
    public string Summary { get; set; }
    public bool IsCircleMember { get; set; }
    public bool PermissionKeysAreValid { get; set; }

    public RedactedPermissionSet ExpectedPermissionKeys { get; set; }
    public RedactedPermissionSet ActualPermissionKeys { get; set; }
    
    public List<DriveGrantInfo> DriveGrantAnalysis { get; set; }

}