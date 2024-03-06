using Odin.Core.Identity;

namespace Odin.Services.Drives.Reactions;

public class GetReactionsByIdentityRequest
{
    public OdinId Identity { get; set; }
    public ExternalFileIdentifier File { get; set; }
}