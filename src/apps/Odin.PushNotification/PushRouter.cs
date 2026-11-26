using Odin.Core.Dto;
using Odin.PushNotification.Apns;

namespace Odin.PushNotification;

public interface IPushRouter
{
    Task<string> SendAsync(DevicePushNotificationRequestV1 request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The one place platform specifics are chosen, right before sending. The API above this only
/// says what kind of push it is (<see cref="PushKind"/>) and which tokens the device registered.
/// A Ring to a device that registered a PushKit token goes out as a VoIP push when the Apple key
/// is configured; everything else, including a Ring without a VoIP token or without the key, goes
/// through Firebase as an ordinary alert (Android shows its own call UI; iOS gets a time-sensitive
/// alert). A Hangup to a device that rang through CallKit goes out as a silent background push: an
/// alert would have no ring alert to replace and its text would show on its own.
/// </summary>
public sealed class PushRouter(IPushNotification firebase, IApnsVoipSender apns, ILogger<PushRouter> logger) : IPushRouter
{
    public Task<string> SendAsync(DevicePushNotificationRequestV1 request, CancellationToken cancellationToken = default)
    {
        var hasVoipToken = !string.IsNullOrWhiteSpace(request.VoipDeviceToken);
        var kind = request.KindOrNotify();
        var wantsVoip = kind == PushKind.Ring && hasVoipToken;

        if (wantsVoip && apns.IsConfigured)
        {
            return apns.SendVoipAsync(request, cancellationToken);
        }

        if (wantsVoip)
        {
            // The stopping point until the Apple key exists: see ApnsOptions for what is needed.
            logger.LogWarning(
                "VoIP push wanted for a Ring to {to} (device registered a PushKit token) but APNs is not configured; sending an alert push instead",
                request.ToDomain);
        }

        // Only when the ring really was a VoIP push; without the Apple key it was an alert and this hangup replaces it.
        if (kind == PushKind.Hangup && hasVoipToken && apns.IsConfigured)
        {
            request.Silent = true;
        }

        return firebase.Post(request);
    }
}
