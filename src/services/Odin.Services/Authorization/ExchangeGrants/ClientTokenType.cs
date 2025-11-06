namespace Odin.Services.Authorization.ExchangeGrants;

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

    Other = 3,

    /// <summary>
    /// The bearer was granted access to send data to this identity 
    /// </summary>
    DataProvider = 7,

    /// <summary>
    /// The bearer was granted access to send data to this identity 
    /// </summary>
    Follower = 14,

    /// <summary>
    /// The bearer is granted access to a built-in browser-based app of the identity server(i.e. the home app)
    /// </summary>
    BuiltInBrowserApp = 209,

    /// <summary>
    /// The bearer is granted this when s/he is listening to app notifications from a remote identity 
    /// </summary>
    RemoteNotificationSubscriber = 254,

    AutomatedPasswordRecovery = 4
}