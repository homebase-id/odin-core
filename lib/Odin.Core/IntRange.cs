namespace Odin.Core;

public class IntRange
{
    public IntRange(int start, int end)
    {
        this.Start = start;
        this.End = end;
    }
    
    public int Start { get; set; }
    public int End { get; set; }


    public bool IsValid()
    {
        return this.Start <= this.End;
    }

    public void Validate()
    {
        if (!IsValid())
        {
            //TODO: change to youverse data exception
            throw new System.IO.InvalidDataException("Start date must be less than end date");
        }
    }
}