using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

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

public class TransitGetDrivesByTypeRequest : GetDrivesByTypeRequest
{
    public string DotYouId { get; set; }
}