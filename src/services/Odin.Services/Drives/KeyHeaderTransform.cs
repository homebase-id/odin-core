#nullable enable
using System;
using Odin.Core.Exceptions;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives;

/// <summary>
/// One file of a transform: the zkDrive key header a key holder made of the deposit it opened, and the version tag it
/// read alongside the sealed one (<see cref="ZkDriveFiles.TransformedKeyHeader"/>).
/// </summary>
public record KeyHeaderTransform(Guid FileId, Guid VersionTag, EncryptedKeyHeader KeyHeader);

/// <summary>
/// One file's answer: the key header now stored, or why it wasn't replaced
/// </summary>
public record KeyHeaderTransformResult(Guid FileId, EncryptedKeyHeader? KeyHeader, OdinClientErrorCode? ErrorCode);
