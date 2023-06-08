namespace Odin.Core.Services.Base;

public class OdinAppContext
{
    public string CorsAppName { get; init; }
    
    /// <summary>
    /// The app client's access registration id
    /// </summary>
    public GuidId AccessRegistrationId { get; init; }
}