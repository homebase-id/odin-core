using System;
using System.Collections.Generic;
using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;

namespace Youverse.Core.Services.Mediator;

public class DriveFileAddedNotification : EventArgs, INotification, IDriveClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.FileAdded;
    
    public InternalDriveFileId File { get; set; }

    public ServerFileHeader ServerFileHeader { get; set; }
}


public class AppRegistrationChangedNotification : EventArgs, INotification
{
    public AppRegistration NewAppRegistration { get; set; }
    public AppRegistration OldAppRegistration { get; set; }
}