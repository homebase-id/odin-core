using System;
using System.Collections.Generic;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class ChatGroup
{
    public const int GroupDefinitionFileType = 8322;

    public Guid Id { get; set; }
    
    public string Title { get; set; }
    
    public string AdminDotYouId { get; set; }
    
    public List<string> Members { get; set; }
}