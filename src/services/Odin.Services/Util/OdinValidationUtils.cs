using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Services.Util;

public static class OdinExtensions
{
    public static List<OdinId> ToOdinIdList(this List<string> items)
    {
        return items.Select(r => (OdinId)r).ToList();
    }

    public static List<OdinId> Without(this List<OdinId> list, OdinId identity)
    {
        return list.Where(r => r != identity).ToList();
    }

    public static List<string> ToDomainNames(this List<OdinId> items)
    {
        return items.Select(r => r.DomainName).ToList();
    }
}

public static class OdinValidationUtils
{
    const string ValidFilenamePattern = @"^[a-zA-Z0-9](?:[a-zA-Z0-9 ._-]*[a-zA-Z0-9])?\.[a-zA-Z0-9_-]+$";

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

    public static void AssertNotNull(object o, string name)
    {
        if (o == null)
        {
            throw new OdinClientException($"{name} is null", OdinClientErrorCode.ArgumentError);
        }
    }

    public static void AssertIsTrue(bool value, string message)
    {
        if (!value)
        {
            throw new OdinClientException(message, OdinClientErrorCode.ArgumentError);
        }
    }

    public static void AssertNotEmptyGuid(Guid g, string name)
    {
        if (g == Guid.Empty)
        {
            throw new OdinClientException($"{name} is empty", OdinClientErrorCode.ArgumentError);
        }
    }

    public static void AssertNotNullOrEmpty(string o, string name)
    {
        if (string.IsNullOrEmpty(o) || string.IsNullOrWhiteSpace(o))
        {
            throw new OdinClientException($"{name} is null", OdinClientErrorCode.ArgumentError);
        }
    }

    public static void AssertValidFileName(string filename, string message)
    {
        if (!Regex.IsMatch(filename, ValidFilenamePattern, RegexOptions.IgnoreCase))
        {
            throw new OdinClientException(message, OdinClientErrorCode.ArgumentError);
        }
    }
}