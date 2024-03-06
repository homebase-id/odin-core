namespace Odin.Services.Base.SharedTypes;

public abstract class GetThumbnailRequestBase
{
    public int Width { get; set; }
    public int Height { get; set; }

    public bool DirectMatchOnly { get; set; } = false;

    public string PayloadKey { get; set; }
}