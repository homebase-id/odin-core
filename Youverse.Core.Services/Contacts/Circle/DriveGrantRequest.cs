using System;
using System.Collections.Generic;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Contacts.Circle;

public class DriveGrantRequest : IEqualityComparer<DriveGrantRequest>
{
    public TargetDrive Drive { get; set; }

    /// <summary>
    /// The type of access allowed for this drive grant
    /// </summary>
    public DrivePermission Permission { get; set; }

    public bool Equals(DriveGrantRequest x, DriveGrantRequest y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null)) return false;
        if (ReferenceEquals(y, null)) return false;
        if (x.GetType() != y.GetType()) return false;
        return Equals(x.Drive, y.Drive) && x.Permission == y.Permission;
    }

    public int GetHashCode(DriveGrantRequest obj)
    {
        return HashCode.Combine(obj.Drive, (int)obj.Permission);
    }
}