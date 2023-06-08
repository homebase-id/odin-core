using System.Collections.Generic;

namespace Youverse.Core;

public class CursoredResult<TResult> : CursoredResult<string, TResult>{}

public class CursoredResult<TCursor, TResult>
{
    public TCursor Cursor { get; set; }

    public IEnumerable<TResult> Results { get; set; }
}