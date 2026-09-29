using Odin.Core;

namespace Odin.Services.Membership.Connections;

/// <summary>
/// A circle membership that stands in the way of clearing a connection's review, named in the
/// <c>blockingCircles</c> field of the rejection.
/// </summary>
/// <remarks>
/// <see cref="CircleId"/> is the same shape the circle-definition API serves, so a client can match it
/// against a circle it already holds without parsing anything.  <see cref="Name"/> is for showing the
/// user; it is not unique and must not be matched on.
/// </remarks>
public class BlockingCircle
{
    public GuidId CircleId { get; init; }

    public string Name { get; init; }
}
