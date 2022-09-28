using System;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class CreateGroupCommand : CommandBase
{
    public string Title { get; set; }
    public Guid GroupId { get; set; }
}