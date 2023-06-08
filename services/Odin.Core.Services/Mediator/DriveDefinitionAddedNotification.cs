using System;
using MediatR;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Mediator;

public class DriveDefinitionAddedNotification : EventArgs, INotification
{
    public bool IsNewDrive { get; set; }
    public StorageDrive Drive { get; set; }
}