using System.Collections.Generic;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Contacts.Circle.Requests;

public class AcceptRequestHeader
{
    public string Sender { get; set; }
        

    public IEnumerable<ByteArrayId> CircleIds { get; set; }
}