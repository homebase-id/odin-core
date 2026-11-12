using System;

namespace Odin.Hosting.UnifiedV2.Authentication;

/// <summary>
/// A read of one drive (<c>{driveId}</c> in its route) that a connected identity's device may make with its
/// peer-notification token, straight from its own server's peer (<see cref="Handlers.PeerSubscriberAuthPathHandler"/>).
/// Only actions that change nothing carry it: a route opts in.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PeerDirectReadAttribute : Attribute;
