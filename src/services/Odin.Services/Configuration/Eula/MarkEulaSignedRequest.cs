namespace Odin.Services.Configuration.Eula;

public class MarkEulaSignedRequest
{
    public string Version { get; set; }
    
    public byte[] SignatureBytes { get; set; } 
    
}