using System.Collections.Generic;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.AppNotifications;

public class EstablishConnectionRequest
{
    /// <summary>
    /// List of drives from which the app wants notifications
    /// </summary>
    public List<TargetDrive> Drives { get; set; }
}