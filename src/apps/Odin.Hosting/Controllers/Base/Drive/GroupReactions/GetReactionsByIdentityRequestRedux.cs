using Odin.Core.Identity;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.Base.Drive.GroupReactions;

public class GetReactionsByIdentityRequestRedux
{
    public string Identity { get; set; }
    public FileIdentifier File { get; set; }
}