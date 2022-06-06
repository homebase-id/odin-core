using System;

namespace Youverse.Core;

public class TimeRange
{
    public UInt64 Start { get; set; }
    public UInt64 End { get; set; }

    
    public bool IsValid()
    {
        return this.Start < this.End;
    }

    public void Validate()
    {
        if (!IsValid())
        {
            //TODO: change to youverse data exception
            throw new InvalidDataException("Start date must be less than end date");
        }
    }
}