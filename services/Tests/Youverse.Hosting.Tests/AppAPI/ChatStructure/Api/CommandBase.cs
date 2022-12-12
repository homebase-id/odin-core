using System.Collections.Generic;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class CommandBase
{
    public CommandCode Code { get; set; }

    public List<string> Recipients { get; set; }
}