namespace Odin.Core.Services.Base;

public class OdinYouAuthContext
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