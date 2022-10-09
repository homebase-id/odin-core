namespace Youverse.Core.Services.Apps.CommandMessaging;

public static class ReservedFileTypes
{
    private const int Start = 2100000000;
    private const int End = int.MaxValue;

    public const int CommandMessage = 2100000001;

    public static bool IsInReservedRange(int value)
    {
        return value is < End and >= Start;
    }
}