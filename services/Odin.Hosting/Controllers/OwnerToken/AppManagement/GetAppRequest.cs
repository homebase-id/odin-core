using System;
using Youverse.Core;

namespace Youverse.Hosting.Controllers.OwnerToken.AppManagement;

public class GetAppRequest
{
    public GuidId AppId { get; set; }
}

public class GetAppClientRequest
{
    public GuidId AccessRegistrationId { get; set; }
}