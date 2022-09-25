using System.Collections.Generic;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Provisioning;

/// <summary>
/// Set of parameters specifying how initial setup should be executed
/// </summary>
public class InitialSetupRequest
{
    /// <summary>
    /// Drives to be created
    /// </summary>
    public List<CreateDriveRequest> Drives { get; set; }
}