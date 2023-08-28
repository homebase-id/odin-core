namespace Odin.Core.Services.Base;

public class OdinYouAuthClientContext
{
    /// <summary>
    /// 
    /// </summary>
    public string CorsHostName { get; init; }
    
    /// <summary>
    /// The app client's access registration id
    /// </summary>
    public GuidId AccessRegistrationId { get; init; }
}