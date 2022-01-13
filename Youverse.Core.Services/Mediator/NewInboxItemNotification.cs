using System;
using MediatR;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Mediator
{
    public class NewInboxItemNotification: INotification
    {
        public Guid InboxItemId { get; set; }
        
        public Guid AppId { get; set; }

        public DotYouIdentity Sender { get; set; }

        public DriveFileId TempFile { get; set; }
    }
}