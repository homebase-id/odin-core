using System.Threading.Tasks;

namespace Odin.Core.Storage.RepositoryPattern.Repositories.System;

public interface IJobRepositoryStrategy
{
    Task<int> SpecializedQueryThatUsesNonPortableSql();
}
