using Odin.Core.Exceptions;

namespace Odin.Core.Time;

public class UnixTimeUtcRange
{
    public UnixTimeUtc Start { get; set; }
    public UnixTimeUtc End { get; set; }

    public UnixTimeUtcRange(UnixTimeUtc start, UnixTimeUtc end)
    {
        if (start > end)
        {
            throw new OdinSystemException("Start date must be less than end date");
        }

        Start = start;
        End = end;
    }

    public bool IsValid()
    {
        return this.Start < this.End;
    }

    public void Validate()
    {
        if (!IsValid())
        {
            throw new OdinSystemException("Start date must be less than end date");
        }
    }
}
