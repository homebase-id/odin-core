using Youverse.Core.Services.Contacts.Circle.Membership;

namespace Youverse.Hosting.Controllers.ClientToken.Circles;

public class ConnectionInfoResponse
{
    public ConnectionStatus Status { get; set; }
    public long LastUpdated { get; set; }
    public bool GrantIsRevoked { get; set; }
}