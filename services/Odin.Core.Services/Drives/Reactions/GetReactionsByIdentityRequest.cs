using Youverse.Core.Identity;

namespace Youverse.Core.Services.Drives.Reactions;

public class GetReactionsByIdentityRequest
{
    public OdinId Identity { get; set; }
    public ExternalFileIdentifier File { get; set; }
}