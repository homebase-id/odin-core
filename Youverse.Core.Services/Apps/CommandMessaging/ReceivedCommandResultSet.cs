using System.Collections.Generic;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Apps.CommandMessaging;

public class ReceivedCommandResultSet
{
    public TargetDrive TargetDrive { get; set; }

    public IEnumerable<ReceivedCommand> ReceivedCommands { get; set; }
}