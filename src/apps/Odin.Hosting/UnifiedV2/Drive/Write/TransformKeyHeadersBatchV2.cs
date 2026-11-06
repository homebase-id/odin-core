using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Services.Peer.Encryption;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class TransformKeyHeadersRequestV2
{
    /// <summary>
    /// The deposits to transform, each with the version tag read alongside its sealed key header. At most
    /// <see cref="FileVersionTagBatchV2.MaxFiles"/>.
    /// </summary>
    public List<TransformKeyHeaderV2> Files { get; init; }

    public void AssertValid()
    {
        FileVersionTagBatchV2.AssertValid(Files);
        OdinValidationUtils.AssertIsTrue(Files.All(f => f.KeyHeader != null), "every file needs a keyHeader");
    }
}

public class TransformKeyHeaderV2 : FileVersionTagV2
{
    /// <summary>
    /// Required. The file's key header locked with the drive's key (type zkDrive), with the IV of the sealed one it
    /// replaces. The server can't check that it locks the same file key: open it before sending it.
    /// </summary>
    public EncryptedKeyHeader KeyHeader { get; init; }
}

public class TransformKeyHeadersResultV2
{
    /// <summary>
    /// One entry per requested file, in request order.
    /// </summary>
    public List<TransformKeyHeaderResultV2> Results { get; init; }
}

public class TransformKeyHeaderResultV2
{
    public Guid FileId { get; init; }

    /// <summary>
    /// The key header now stored; the file's version tag and modified time are unchanged. Null when
    /// <see cref="ErrorCode"/> is set.
    /// </summary>
    public EncryptedKeyHeader KeyHeader { get; init; }

    /// <summary>
    /// Null on success. Otherwise why this file's key header wasn't replaced, as a camelCase string like every
    /// OdinClientErrorCode: "versionTagMismatch" (the file changed, or was hard-deleted, since it was read),
    /// "notSealed" (already transformed, or never a deposit), "fileNotFound", "cannotUpdateNonActiveFile" (deleted),
    /// "invalidKeyHeader" (not a zkDrive key header, or another IV), or any other client error the file's read raised.
    /// One file's error never stops the others.
    /// </summary>
    public OdinClientErrorCode? ErrorCode { get; init; }
}
