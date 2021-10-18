using System;

namespace Youverse.Core.Identity.DataAttribute
{
    /// <summary>
    /// These access right flags are for attributes. For each actor on an attribute, these
    /// flags define the rights. 
    /// </summary>
    [Flags]
    public enum PermissionFlags
    {
        None = 0,
        Read = 1
    }
}