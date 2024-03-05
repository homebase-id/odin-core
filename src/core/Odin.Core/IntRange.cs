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
}