#nullable enable
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.App;

public class GetUnproccessedCommandsRequest
{
    public TargetDrive TargetDrive { get; set; }
    public string Cursor { get; set; }
}