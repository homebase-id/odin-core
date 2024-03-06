using Odin.Core.Time;

namespace Odin.Services.Optimization.Cdn;

public class StaticFileConfiguration
{
    /// <summary>
    /// Specifies how to handle the CORS header for a given file
    /// </summary>
    public CrossOriginBehavior CrossOriginBehavior { get; set; }
    
    /// <summary>
    /// Specifies the content type header for the file.  This is ignored when set from the client.
    /// </summary>
    public string ContentType { get; set; }
    
    /// <summary>
    /// Set by server
    /// </summary>
    public UnixTimeUtc LastModified { get; set; }
}
