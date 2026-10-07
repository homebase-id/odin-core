using System;

namespace Odin.Hosting.Controllers.OwnerToken.AppManagement;

public class UninstallAppRequest
{
    public Guid AppId { get; set; }

    /// <summary>
    /// Deletes the circles and drives the app owns along with it. Required when it owns any.
    /// </summary>
    public bool DeleteOwnedCirclesAndDrives { get; set; }
}
