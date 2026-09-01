using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Apps.Builtin;

namespace Odin.Services.Membership.Circles;

/// <summary>
/// Built-in circles are provisioned for every identity (like system circles), but unlike system
/// circles they are not hidden or master-key-gated — the owner manages their membership as they
/// would any normal circle. They simply already exist with the identity out of the box.
/// </summary>
/// <remarks>
/// Identity only.  What these circles <i>are</i> is declared once, in <see cref="BuiltinCircles"/>, and
/// created once, by <c>CircleDefinitionService.EnsureCircleExistsAsync</c>.  This file used to carry a
/// second definition of Emergency Location Access whose creation dropped the owning app.
/// </remarks>
public static class BuiltInCircleConstants
{
    public static readonly GuidId EmergencyLocationAccessCircleId = BuiltinCircles.EmergencyLocationAccessCircle.Id;
    public static readonly List<GuidId> AllBuiltInCircles =
    [
        EmergencyLocationAccessCircleId
    ];
}
