namespace Odin.Services.Membership.Connections.Requests;

public enum ConnectionRequestOrigin
{
    /// <summary>
    /// Indicates the connection request was sent by the identity owner
    /// </summary>
    IdentityOwner,
        
    /// <summary>
    /// Indicates the connection request came because another identity introduce you to the recipient
    /// </summary>
    Introduction
}