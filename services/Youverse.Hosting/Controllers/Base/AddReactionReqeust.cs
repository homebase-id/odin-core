using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.Base;

public class AddReactionReqeust
{
    public string Reaction { get; set; }
    public ExternalFileIdentifier File { get; set; }
}


public class DeleteReactionRequest
{
    public string Reaction { get; set; }
    public ExternalFileIdentifier File { get; set; }
}