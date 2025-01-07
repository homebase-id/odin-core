#nullable enable
using Odin.Services.Base;

namespace Odin.Services.AppNotifications.WebSocket;

// ReSharper disable once ClassNeverInstantiated.Global
public class SocketAuthenticationPackage
{
    public string ClientAuthToken64 { get; set; } = "";
    public SharedSecretEncryptedPayload? SharedSecretEncryptedOptions { get; set; } = default;
}