﻿using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Youverse.Core.SystemStorage
{
    public interface IStorage<T> : IDisposable
    {
        Task Save(T item);
        Task<T> Get(Guid id);

        Task<bool> Delete(Guid id);

        Task<PagedResult<T>> Find(Expression<Func<T, bool>> predicate, PageOptions req);

        Task<PagedResult<T>> GetList(PageOptions req);

    }
}