using System;

namespace Odin.Services.Membership.YouAuth;

public class YouAuthDomainClientRegistrationResponse
{
    public Guid AccessRegistrationId { get; set; }
    /// <summary>
    /// RSA encrypted response.  When encryption version == 1, the  first 16 bytes is token id, second 16 bytes is AccessTokenHalfKey, and last 16 bytes is SharedSecret
    /// </summary>
    public byte[] Data { get; set; }
}