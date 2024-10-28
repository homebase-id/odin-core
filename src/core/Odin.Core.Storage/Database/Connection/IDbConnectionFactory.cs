using System.Data.Common;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database.Connection;

#nullable enable

public interface IDbConnectionFactory
{
    // SEB:NOTE We can't use IDbConnection because it lacks async methods
    Task<DbConnection> CreateAsync();
}