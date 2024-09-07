namespace Odin.Services.Membership.Connections.Requests;

public class IcrVerificationResult
{
    /// <summary>
    /// Indicates the connection between the two identities is valid
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// If true, indicates the remote identity considered the caller as connected; even if the connection
    /// was invalid.  Null indicates the call was never made to the remote identity
    /// </summary>
    public bool? RemoteIdentityWasConnected { get; set; }
}