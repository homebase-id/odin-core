#nullable enable
using System;
using System.Collections.Generic;
using Youverse.Core.Services.Apps.CommandMessaging;

namespace Youverse.Hosting.Controllers.ClientToken.App;

public class MarkCommandsCompleteRequest
{
    public IEnumerable<CommandId> CommandIdList { get; set; }
}