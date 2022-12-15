using System;
using System.Collections.Generic;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Configuration;

/// <summary>
/// Set of parameters specifying how initial setup should be executed
/// </summary>
public class InitialSetupRequest
{
    
    public Guid? FirstRunToken { get; set; }
    
    /// <summary>
    /// Drives to be created
    /// </summary>
    public List<CreateDriveRequest> Drives { get; set; }

    public List<CreateCircleRequest> Circles { get; set; }
}