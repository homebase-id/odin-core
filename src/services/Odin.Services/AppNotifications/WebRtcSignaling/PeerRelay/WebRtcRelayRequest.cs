using System;

namespace Odin.Services.AppNotifications.WebRtcSignaling.PeerRelay;

// Body of POST /api/peer/v1/host/webrtc/relay. The sending server forwards the typed
// signaling message here; the receiving server delivers it to the recipient's open
// sockets. SignalType drives which fields are populated — sdp/candidate/sdpMid/
// sdpMLineIndex are only meaningful for offer/answer/ice. Unused fields stay null.
public class WebRtcRelayRequest
{
    public WebRtcSignalType SignalType { get; init; }
    public Guid CallId { get; init; }
    public string Sdp { get; init; }
    public string Candidate { get; init; }
    public string SdpMid { get; init; }
    public int? SdpMLineIndex { get; init; }
}

public enum WebRtcSignalType
{
    Invite = 1,
    Offer = 2,
    Answer = 3,
    Ice = 4,
    Hangup = 5,
    Reject = 6,
}
