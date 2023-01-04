using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Youverse.Core.Services.AppNotifications
{
    /// <summary>
    /// All devices connected for app notifications
    /// </summary>
    public class DeviceSocketCollection
    {
        private ConcurrentDictionary<Guid, DeviceSocket> _sockets = new ConcurrentDictionary<Guid, DeviceSocket>();

        public ConcurrentDictionary<Guid, DeviceSocket> GetAll()
        {
            return _sockets;
        }
        

        public void AddSocket(DeviceSocket socket)
        {
            _sockets.TryAdd(socket.Key, socket);
        }

        public async Task RemoveSocket(Guid key)
        {
            _sockets.TryRemove(key, out var deviceSocket);

            try
            {
                await deviceSocket.Socket.CloseAsync(closeStatus: WebSocketCloseStatus.NormalClosure,
                    statusDescription: "Closed by the ConnectionManager",
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception e)
            {
                //TODO: swallowing: System.Net.WebSockets.WebSocketException The remote party closed the WebSocket connection without completing the close handshake.
                //---> System.ObjectDisposedException: Cannot write to the response body, the response has completed.

                //I think this occurs because the client has already moved on.
                Console.WriteLine(e);
            }
        }
    }
}