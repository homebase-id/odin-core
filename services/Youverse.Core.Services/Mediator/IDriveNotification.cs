using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;

namespace Youverse.Core.Services.Mediator;

public interface IDriveNotification : INotification
{
    ClientNotificationType NotificationType { get; }
        
    public InternalDriveFileId File { get; set; }
        
    public ServerFileHeader ServerFileHeader { get; set; }
    
    public ClientFileHeader ClientFileHeader { get; set; }

}