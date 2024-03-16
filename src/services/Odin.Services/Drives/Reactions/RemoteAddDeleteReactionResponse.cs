using Odin.Core.Identity;

namespace Odin.Services.Drives.Reactions;

public enum AddDeleteReactionStatusCode
{
    Failure = 0,
    Success = 1
}

public class RemoteAddDeleteReactionResponse
{
    public OdinId Recipient { get; set; }
    public AddDeleteReactionStatusCode Status { get; set; }
}