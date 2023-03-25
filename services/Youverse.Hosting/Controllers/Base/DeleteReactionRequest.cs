using Youverse.Core.Services.Drives;

namespace Youverse.Hosting.Controllers.Base;

public class DeleteReactionRequest
{
    public string Reaction { get; set; }
    public ExternalFileIdentifier File { get; set; }
}