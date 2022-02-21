using System;
using MediatR;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Mediator.ClientNotifications
{
    public class NewInboxItemNotification : INotification, IOwnerConsoleNotification
    {
        public string Key => "NewInboxItem";

        public Guid InboxItemId { get; set; }

        public Guid AppId { get; set; }

        public DotYouIdentity Sender { get; set; }

        public DriveFileId TempFile { get; set; }
    }
}