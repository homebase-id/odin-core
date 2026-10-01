using System;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.LiveRelay;

public static class LiveRelayPush
{
    /// <summary>
    /// The push a LiveRelay message may carry, reduced to what the recipient's push pipeline gets:
    /// an allowlist copy, so a field added to <see cref="AppNotificationOptions"/> later does not
    /// travel peer-to-recipient unseen. The app id is the relay's, never the sender's claim; the
    /// fan-out fields (Recipients, PeerSubscriptionId) have no meaning here and are left unset.
    /// Applied at hop 1 (what goes on the wire) and again at hop 2 (what a peer may have sent).
    /// </summary>
    public static AppNotificationOptions Sanitize(AppNotificationOptions push, Guid appId) => new()
    {
        AppId = appId,
        Kind = push.Kind,
        TypeId = push.TypeId,
        TagId = push.TagId,
        UnEncryptedMessage = push.UnEncryptedMessage,
        Silent = push.Silent,
        TimeToLiveSeconds = push.TimeToLiveSeconds,
        CollapseId = push.CollapseId,
        TimeSensitive = push.TimeSensitive,
    };
}
