using System.Data.Common;
using System.Threading.Tasks;

namespace Odin.Core.Storage.RepositoryPattern.Connection;

#nullable enable

public interface IDbConnectionFactory
{
    // NOTE: We can't use IDbConnection because it lacks async methods
    Task<DbConnection> CreateAsync();
}


