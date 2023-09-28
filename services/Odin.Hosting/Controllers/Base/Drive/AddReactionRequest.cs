using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive;

public class AddReactionRequest
{
    public string Reaction { get; set; }
    public ExternalFileIdentifier File { get; set; }
}