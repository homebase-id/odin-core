using Odin.Core;

namespace Odin.Hosting.Controllers.OwnerToken.AppManagement;

public class GetAppRequest
{
    public GuidId AppId { get; set; }
}

public class GetAppClientRequest
{
    public GuidId AccessRegistrationId { get; set; }
}