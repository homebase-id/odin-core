namespace Odin.Core.Services.Base;

public class OdinYouAuthClientContext
{
    /// <summary>
    /// The host name used for CORS, if any
    /// </summary>
    public string CorsHostName { get; init; }
    
    /// <summary>
    /// The app client's access registration id
    /// </summary>
    public GuidId AccessRegistrationId { get; init; }

    public string ClientIdOrDomain { get; set; }
}