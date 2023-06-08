using System.Collections.Generic;
using Dawn;

namespace Odin.Core.Services.Contacts.Circle.Requests;

public class AcceptRequestHeader
{
    public string Sender { get; set; }
    
    public IEnumerable<GuidId> CircleIds { get; set; }
    
    /// <summary>
    /// Initial data sent with a connection request
    /// </summary>
    public ContactRequestData ContactData { get; set; }

    public void Validate()
    {
        Guard.Argument(Sender, nameof(Sender)).NotEmpty().NotNull();
        Guard.Argument(ContactData, nameof(ContactData)).NotNull();
        ContactData.Validate();
    }
}