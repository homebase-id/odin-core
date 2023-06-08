using System.Collections.Generic;

namespace Odin.Core.Services.Apps.CommandMessaging;

public class ReceivedCommandResultSet
{

    public IEnumerable<ReceivedCommand> ReceivedCommands { get; set; }
}