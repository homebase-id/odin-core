#nullable enable
using Odin.Services.Base;

namespace Odin.Services.AppNotifications.WebSocket;

public class SocketAuthenticationPackage
{
    public string ClientAuthToken64 { get; set; } = "";
    public SharedSecretEncryptedPayload? SharedSecretEncryptedOptions { get; set; } = default;
}