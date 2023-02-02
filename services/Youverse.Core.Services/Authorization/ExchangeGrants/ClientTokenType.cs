namespace Youverse.Core.Services.Authorization.ExchangeGrants;

public enum ClientTokenType
{
    /// <summary>
    /// The bearer was granted access to authenticate to public content for this identity
    /// </summary>
    YouAuth = 1,

    IdentityConnectionRegistration = 2,

    Other = 3,

    /// <summary>
    /// The bearer was granted access to send data to this identity 
    /// </summary>
    DataProvider = 7
}