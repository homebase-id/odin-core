using System;
using System.Collections.Generic;

namespace Odin.Hosting.Tests.AppAPI.ChatStructure.Api;

public class ChatGroup
{
    public const int GroupDefinitionFileType = 8322;

    public Guid Id { get; set; }
    
    public string Title { get; set; }
    
    public string AdminOdinId { get; set; }
    
    public List<string> Members { get; set; }
}