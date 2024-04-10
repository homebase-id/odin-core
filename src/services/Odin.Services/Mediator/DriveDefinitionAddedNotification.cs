using System;
using MediatR;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Mediator;

public class DriveDefinitionAddedNotification(OdinContext context) : MediatorNotificationBase(context)
{
    public bool IsNewDrive { get; set; }
    public StorageDrive Drive { get; set; }
}