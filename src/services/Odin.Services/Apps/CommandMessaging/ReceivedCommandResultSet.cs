using System.Collections.Generic;

namespace Odin.Services.Apps.CommandMessaging;

public class ReceivedCommandResultSet
{

    public IEnumerable<ReceivedCommand> ReceivedCommands { get; set; }
}