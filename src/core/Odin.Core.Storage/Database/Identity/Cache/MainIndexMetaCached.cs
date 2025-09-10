using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Cache;

#nullable enable

// public class MainIndexMetaCached(
//     MainIndexMeta meta,
//     ITenantLevel2Cache cache,
//     ScopedIdentityConnectionFactory scopedConnectionFactory) :
//     AbstractTableCaching(cache, scopedConnectionFactory, TableDriveMainIndexCached.SharedRootTag)
// {
//
// }
