using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Controllers;

public class TransitGetThumbRequest : GetThumbnailRequest
{
    public string OdinId { get; set; }
}

public class TransitExternalFileIdentifier
{
    public string OdinId { get; set; }

    public ExternalFileIdentifier File { get; set; }
}

public class TransitGetDrivesByTypeRequest : GetDrivesByTypeRequest
{
    public string OdinId { get; set; }
}