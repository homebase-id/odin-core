using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Drive.Query;

public class QueryParams
{
    public IEnumerable<int> FileType { get; set; }= null;
    public IEnumerable<int> DataType { get; set; } = null;
    public IEnumerable<byte[]> Sender { get; set; } = null;
    
    //TODO: implement in indexer
    // public IEnumerable<byte[]> ThreadId { get; set; } = null;
    
    public TimeRange UserDate { get; set; } = null;
    
    public IEnumerable<byte[]> TagsMatchAtLeastOne { get; set; } = null;
    
    public IEnumerable<byte[]> TagsMatchAll { get; set; } = null;
}