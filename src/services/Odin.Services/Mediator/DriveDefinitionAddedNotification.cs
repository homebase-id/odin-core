using System;
using MediatR;
using Odin.Services.Drives;

namespace Odin.Services.Mediator;

public class DriveDefinitionAddedNotification : EventArgs, INotification
{
    public bool IsNewDrive { get; set; }
    public StorageDrive Drive { get; set; }
}