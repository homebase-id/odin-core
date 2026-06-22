using System;
using Odin.Services.Contacts;
using Odin.Services.Util;

namespace Odin.Services.Membership.Connections.Requests;

/// <summary>
/// Initial data sent with a connection request
/// </summary>
public class ContactRequestData
{
    /// <summary>
    /// The name to be shown the recipient on the request
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// An image to be sent with the connection request
    /// </summary>
    public Guid ImageId { get; set; }

    /// <summary>
    /// The peer-sourceable contact card (name/location/phone/email/birthday/source) shared with the peer.
    /// Optional and additive for backward compatibility: older peers send only <see cref="Name"/>,
    /// and the contact is then created from that. The receiving side can materialize a rich contact
    /// from it when it holds the ContactDrive storage key. Typed as <see cref="PeerContactContent"/> so a
    /// peer can never set owner-owned fields (e.g. <c>IsEmergencyContact</c>) over this channel.
    /// </summary>
    public PeerContactContent Contact { get; set; }
}
