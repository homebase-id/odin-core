using Odin.Core.Identity;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.Base.Drive.ReactionsRedux;

public class GetReactionsByIdentityRequestRedux
{
    public OdinId Identity { get; set; }
    public FileIdentifier File { get; set; }
}