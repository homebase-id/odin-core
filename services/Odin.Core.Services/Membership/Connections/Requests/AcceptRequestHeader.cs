using System.Collections.Generic;

using Odin.Core.Services.Base;
using Odin.Core.Services.Util;

namespace Odin.Core.Services.Membership.Connections.Requests;

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
        OdinValidationUtils.AssertNotNullOrEmpty(Sender, nameof(Sender));
        OdinValidationUtils.AssertNotNull(ContactData, nameof(ContactData));
        ContactData.Validate();
    }
}