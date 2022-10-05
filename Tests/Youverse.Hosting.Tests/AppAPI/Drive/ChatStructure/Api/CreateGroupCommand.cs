using System;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class CreateGroupCommand : CommandBase
{
    public ChatGroup ChatGroup { get; set; }
}