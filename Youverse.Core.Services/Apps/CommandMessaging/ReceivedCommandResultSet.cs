using System.Collections.Generic;

namespace Youverse.Core.Services.Apps.CommandMessaging;

public class ReceivedCommandResultSet
{
    public IEnumerable<ReceivedCommand> ReceivedCommands { get; set; }
}