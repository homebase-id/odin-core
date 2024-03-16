using System.Collections.Generic;
using Odin.Hosting.Controllers.Base.Drive;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive;


public class AddGroupReactionRequest
{
    public List<string> Recipients { get; set; }
    public AddReactionRequest Request { get; set; }
}

public class DeleteGroupReactionRequest
{
    public List<string> Recipients { get; set; }

    public DeleteReactionRequest Request { get; set; }
    
}