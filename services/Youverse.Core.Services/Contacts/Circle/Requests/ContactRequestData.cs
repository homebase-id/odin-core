using System;
using Dawn;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Contacts.Circle.Requests;

/// <summary>
/// Initial data sent with a connection request
/// </summary>
public class ContactRequestData
{
    /// <summary>
    /// The name to be shown the recipient on the request
    /// </summary>
    public string GivenName { get; set; }
        
    /// <summary>
    /// The name to be shown the recipient on the request
    /// </summary>
    public string Surname { get; set; }
    
    /// <summary>
    /// An image to be sent with the connection request
    /// </summary>
    public Guid ImageId { get; set; }

    public void Validate()
    {
        Guard.Argument(GivenName, nameof(GivenName)).NotNull().NotEmpty();
        Guard.Argument(Surname, nameof(Surname)).NotNull().NotEmpty();
    }
}