namespace Youverse.Core.Services.Authorization.ExchangeGrants;

public enum ClientTokenType
{
    /// <summary>
    /// The bearer was granted access to authenticate to public content for this identity
    /// </summary>
    YouAuth = 1,

    /// <summary>
    /// The bearer is using the CircleNetwork Connection via transit
    /// </summary>
    IdentityConnectionRegistration = 2,

    Other = 3

}