using System;

using Odin.Core.Services.Util;

namespace Odin.Core.Services.Membership.Connections.Requests;

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

    public void Validate()
    {
        OdinValidationUtils.AssertNotNullOrEmpty(Name, nameof(Name));
    }
}
