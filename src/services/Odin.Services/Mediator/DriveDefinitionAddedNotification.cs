using System;
using Odin.Core.Storage.SQLite;
using Odin.Services.Drives;

namespace Odin.Services.Mediator;

public class DriveDefinitionAddedNotification : MediatorNotificationBase // EventArgs, INotification
{
    public bool IsNewDrive { get; init; }
    public StorageDrive Drive { get; init; }
    public IdentityDatabase db { get; init; }
}