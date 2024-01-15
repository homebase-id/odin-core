namespace Odin.Hosting.Controllers.OwnerToken.Cdn;

public class PublishPublicProfileImageRequest
{
    /// <summary>
    /// Base64 encoded byte array of the image
    /// </summary>
    public string Image64 { get; set; }

    /// <summary>
    ///  The mime-type
    /// </summary>
    public string ContentType { get; set; }
}