namespace Odin.Services.Drives;

public static class ReservedFileTypes
{
    /// <summary>
    /// Start range of reserved file types
    /// </summary>
    public const int Start = 2100000000;

    /// <summary>
    /// End range of reserved file types
    /// </summary>
    public const int End = int.MaxValue;

    public static bool IsInReservedRange(int value)
    {
        return value is < End and >= Start;
    }
}