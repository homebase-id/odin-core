using System;
using System.Collections.Generic;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Configuration;

/// <summary>
/// Set of parameters specifying how initial setup should be executed
/// </summary>
public class InitialSetupRequest
{
    public Guid? FirstRunToken { get; set; }
    
    public bool UseAutomatedPasswordRecovery { get; init; }
    
    /// <summary>
    /// Drives to be created
    /// </summary>
    public List<CreateDriveRequest> Drives { get; set; }

    public List<CreateCircleRequest> Circles { get; set; }
}