using System.Threading.Tasks;
using Odin.Hosting.SignalR.Models;

namespace Odin.Hosting.SignalR;

/// <summary>
/// Strongly-typed interface defining all methods that can be called on the client from the server
/// </summary>
public interface INotificationClient
{
    Task ReceiveTextMessage(TextMessage message);
}
