using System;
using Odin.Core.Exceptions;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

/// <summary>
/// The import was refused and nothing was committed: the file is not a readable identity export, or the
/// target does not accept it (a failed precondition). Anything else the import throws is a fault.
/// </summary>
public class IdentityImportRefusedException(string message, Exception? inner = null) : OdinException(message, inner!);
