using System;
using System.Data;
using System.Threading.Tasks;

namespace Odin.Core.Storage.RepositoryPattern.Connection;

#nullable enable

public interface ILazyDbConnection : IDisposable
{
    Task<IDbConnection> Get();
}


