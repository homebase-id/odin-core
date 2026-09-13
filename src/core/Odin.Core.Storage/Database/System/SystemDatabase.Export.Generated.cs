// THIS FILE IS AUTO GENERATED - DO NOT EDIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Database.System.Table;

#nullable disable

namespace Odin.Core.Storage.Database.System;

public partial class SystemDatabase
{
    public static readonly ImmutableList<string> ExportableTables = [
        "Certificates",
        "DkimKeys",
        "Registrations",
    ];

    public static readonly ImmutableDictionary<string, Type> ExportableRecordTypes =
        new Dictionary<string, Type>
        {
            ["Certificates"] = typeof(CertificatesRecord),
            ["DkimKeys"] = typeof(DkimKeysRecord),
            ["Registrations"] = typeof(RegistrationsRecord),
        }.ToImmutableDictionary();

    public async Task ExportAsync(OdinId domain, Guid identityId, Func<string, object, Task> onRow)
    {
        await Certificates.ExportRowsAsync(domain, async r => await onRow("Certificates", r));
        await DkimKeys.ExportRowsAsync(domain, async r => await onRow("DkimKeys", r));
        await Registrations.ExportRowsAsync(identityId, async r => await onRow("Registrations", r));
    }

    public async Task<int> ImportRowAsync(string tableName, object record)
    {
        switch (tableName)
        {
            case "Certificates":
                return await Certificates.ImportRowAsync((CertificatesRecord)record);
            case "DkimKeys":
                return await DkimKeys.ImportRowAsync((DkimKeysRecord)record);
            case "Registrations":
                return await Registrations.ImportRowAsync((RegistrationsRecord)record);
            default:
                throw new ArgumentException($"Unknown exportable table '{tableName}'", nameof(tableName));
        }
    }

    public async Task<Dictionary<string, long>> GetTableVersionsAsync()
    {
        await using var cn = await CreateScopedConnectionAsync();
        var result = new Dictionary<string, long>();
        foreach (var name in ExportableTables)
        {
            result[name] = await SqlHelper.GetTableVersionAsync(cn, name);
        }
        return result;
    }

}
