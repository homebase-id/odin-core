using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Services.Apps;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives;

/// <summary>
/// What is left of the old drive constants after the app tree took them over.
/// </summary>
/// <remarks>
/// Nearly everything moved: the drives are declared in <c>BuiltinDrives</c>, and the set the owner may
/// not modify is <c>BuiltinDrives.Protected</c>.  What is left is the transient drive's identity, which
/// still has ~96 references, and a forwarder for the channel type.
/// </remarks>
public static class SystemDriveConstants
{
    //
    // DO NOT CHANGE ANY VALUES
    //

    public static readonly Guid ChannelDriveType = WellKnownAppDrives.ChannelDriveType;

    public static readonly TargetDrive TransientTempDrive = new()
    {
        Alias = Guid.Parse("90f5e74ab7f9efda0ac298373a32ad8c"),
        Type = Guid.Parse("90f5e74ab7f9efda0ac298373a32ad8c"),
    };

    
}
