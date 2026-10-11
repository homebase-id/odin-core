namespace Odin.Core.Exceptions;

/// <summary>
/// A payload over the cap for reading it whole into memory. Payloads are unbounded, so a whole read is only made
/// under a cap (#1892); this is that cap refusing one, not a parse or storage failure.
/// </summary>
public class OdinPayloadTooLargeException(string message) : OdinSystemException(message);
