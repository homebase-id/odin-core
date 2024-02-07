using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Util;

public static class OdinValidationUtils
{
    public static void AssertIsValidOdinId(string odinId, out OdinId id)
    {
        if (OdinId.IsValid(odinId))
        {
            id = (OdinId)odinId;
            return;
        }

        throw new OdinClientException("Missing target OdinId", OdinClientErrorCode.ArgumentError);
    }

    public static void AssertIsValidTargetDriveValue(TargetDrive targetDrive)
    {
        if ((targetDrive?.IsValid() ?? false) == false)
        {
            throw new OdinClientException("Invalid target drive", OdinClientErrorCode.InvalidTargetDrive);
        }
    }

    public static void AssertValidRecipientList(IEnumerable<string> recipients, bool allowEmpty = true)
    {
        var list = recipients?.ToArray() ?? [];
        if (list.Length == 0 && !allowEmpty)
        {
            throw new OdinClientException("One or more recipients are required", OdinClientErrorCode.InvalidRecipient);
        }

        foreach (var r in list)
        {
            AssertIsValidOdinId(r, out var _);
        }
    }
}