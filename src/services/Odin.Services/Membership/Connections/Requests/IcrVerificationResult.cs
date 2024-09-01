namespace Odin.Services.Membership.Connections.Requests;

public class IcrVerificationResult
{
    public bool IsValid { get; set; }

    /// <summary>
    /// If true, indicates the remote identity considered the caller as connected; even if the connection was invalid
    /// </summary>
    public bool? RemoteIdentityWasConnected { get; set; }
}