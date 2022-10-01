using System.Collections.Generic;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class CommandBase
{
    public const int FileType = 587;

    public CommandCode Code { get; set; }
    public List<string> Recipients { get; set; }

}