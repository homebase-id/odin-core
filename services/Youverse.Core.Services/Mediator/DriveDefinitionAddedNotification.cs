using System;
using MediatR;
using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.Mediator;

public class DriveDefinitionAddedNotification : EventArgs, INotification
{
    public bool IsNewDrive { get; set; }
    public StorageDrive Drive { get; set; }
}