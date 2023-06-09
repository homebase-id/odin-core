namespace Odin.Core.Time;

public class UnixTimeUtcRange
{
    public UnixTimeUtc Start { get; set; }
    public UnixTimeUtc End { get; set; }

    public UnixTimeUtcRange(UnixTimeUtc start, UnixTimeUtc end)
    {
        if (start > end)
            throw new System.IO.InvalidDataException("Start date must be less than end date");

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
            //TODO: change to Odin data exception
            throw new System.IO.InvalidDataException("Start date must be less than end date");
        }
    }
}