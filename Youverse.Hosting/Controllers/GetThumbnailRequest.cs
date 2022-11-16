using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Controllers;

public class TransitGetThumbRequest : GetThumbnailRequest
{
    public string DotYouId { get; set; }
}

public class TransitExternalFileIdentifier
{
    public string DotYouId { get; set; }

    public ExternalFileIdentifier File { get; set; }
}