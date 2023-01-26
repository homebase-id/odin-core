using System.Collections.Generic;

namespace Youverse.Core;

public class CursoredResult<T>
{
    public CursoredResult()
    {
        
    }
    public CursoredResult(string cursor)
    {
        Cursor = cursor;
    }
    
    public string Cursor { get; set; }
    
    public IEnumerable<T> Results { get; set; }
}