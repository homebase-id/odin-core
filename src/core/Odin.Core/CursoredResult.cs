using System.Collections.Generic;

namespace Odin.Core;

// public class CursoredResult<TResult> : CursoredResult<string, TResult>{}

public class CursoredResult<TResult>
{
    public string Cursor { get; set; }

    public IEnumerable<TResult> Results { get; set; }
}