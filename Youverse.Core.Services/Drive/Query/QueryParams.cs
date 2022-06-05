using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Drive.Query;

public class QueryParams
{
    public IEnumerable<int> FileType { get; set; }= null;
    public IEnumerable<int> DataType { get; set; } = null;
    public IEnumerable<byte[]> Sender { get; set; } = null;
    public IEnumerable<byte[]> ThreadId { get; set; } = null;
    public IEnumerable<UInt64> UserDateSpan { get; set; } = null;
    
    public IEnumerable<byte[]> AclId { get; set; } = null;
    
    public IEnumerable<byte[]> TagsMatchOne { get; set; } = null;
    public IEnumerable<byte[]> TagsMatchAll { get; set; } = null;
}