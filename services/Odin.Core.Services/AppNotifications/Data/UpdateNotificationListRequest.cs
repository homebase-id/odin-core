using System;
using System.Collections.Generic;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.AppNotifications.Data;

public class NotificationData
{
    public ExternalFileIdentifier File { get; set; }
}

public class AddNotificationRequest
{
    public ExternalFileIdentifier File { get; set; }
}
public class UpdateNotificationListRequest
{
    private List<UpdateNotificationRequest> Updates { get; set; }
}

public class UpdateNotificationRequest
{
    public Guid Id { get; set; }

    public bool Read { get; set; }
}