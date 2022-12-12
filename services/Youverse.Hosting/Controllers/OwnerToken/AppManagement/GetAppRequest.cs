using System;
using Youverse.Core;

namespace Youverse.Hosting.Controllers.OwnerToken.AppManagement;

public class GetAppRequest
{
    public GuidId AppId { get; set; }
}