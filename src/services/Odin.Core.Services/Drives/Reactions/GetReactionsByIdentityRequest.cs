using Odin.Core.Identity;

namespace Odin.Core.Services.Drives.Reactions;

public class GetReactionsByIdentityRequest
{
    public OdinId Identity { get; set; }
    public ExternalFileIdentifier File { get; set; }
}