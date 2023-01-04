using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class FileAddedNotification : IOwnerConsoleNotification
    {
        public string Key => "NewInboxItem";
        
        public DotYouIdentity Sender { get; set; }

        public ExternalFileIdentifier TempFile { get; set; }
    }
}