namespace Odin.Services.Membership.Connections.Requests;

public enum ConnectionRequestOrigin
{
    None = 0,

    /// <summary>
    /// Indicates the connection request was sent by the identity owner
    /// </summary>
    IdentityOwner = 1,

    /// <summary>
    /// Indicates the connection request came because another identity introduce you to the recipient
    /// </summary>
    Introduction = 2,
    
    /// <summary>
    /// The request was sent by an app from the owner; meaning there is no master key at the time of sending
    /// </summary>
    IdentityOwnerApp = 3
}