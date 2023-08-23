using Odin.Core.Services.Membership.Connections;

namespace Odin.Hosting.Controllers.ClientToken.Circles;

/// <summary>
/// Information about a connection
/// </summary>
public class ConnectionInfoResponse
{
    public ConnectionStatus Status { get; set; }
    public long LastUpdated { get; set; }
    public bool GrantIsRevoked { get; set; }
}