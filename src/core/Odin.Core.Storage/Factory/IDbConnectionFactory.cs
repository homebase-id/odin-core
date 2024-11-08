using System.Data.Common;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Factory;

#nullable enable

public interface IDbConnectionFactory
{
    Task<DbConnection> CreateAsync();
}

