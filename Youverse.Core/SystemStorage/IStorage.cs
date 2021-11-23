using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Youverse.Core.SystemStorage
{
    public interface IStorage<T> : IDisposable
    {
        Task<PagedResult<T>> GetList(PageOptions req);

        /// <summary>
        /// Retrieves a sorted list
        /// </summary>
        /// <param name="req"></param>
        /// <param name="sortDirection"></param>
        /// <param name="keySelector"></param>
        /// <typeparam name="TK"></typeparam>
        /// <returns></returns>
        Task<PagedResult<T>> GetList<TK>(PageOptions req, ListSortDirection sortDirection, Expression<Func<T, TK>> keySelector);

        /// <summary>
        /// Finds the first record matching the predicate; otherwise null.
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="req"></param>
        /// <returns></returns>
        Task<T> FindOne(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// Finds all records of T using the given predicate.  This is passed directly to LiteDB.
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="req"></param>
        /// <returns></returns>
        Task<PagedResult<T>> Find(Expression<Func<T, bool>> predicate, PageOptions req);

        /// <summary>
        /// Finds all records of T using the given predicate and sorting parameters.  This is passed directly to LiteDB.
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="req"></param>
        /// <param name="sortDirection"></param>
        /// <param name="sortKeySelector"></param>
        /// <returns></returns>
        Task<PagedResult<T>> Find<TK>(Expression<Func<T, bool>> predicate, ListSortDirection sortDirection, Expression<Func<T, TK>> sortKeySelector, PageOptions req);

        /// <summary>
        /// Get an item by Id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> Get(Guid id);
        
        /// <summary>
        /// Save the item
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        Task Save(T item);

        /// <summary>
        /// Updates a set of items matching the <param name="predicate">predicate</param> using the <param name="extend">extend</param>
        /// </summary>
        /// <param name="extend"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        Task<int> UpdateMany(Expression<Func<T, T>> extend, Expression<Func<T, bool>> predicate);
        
        /// <summary>
        /// Delete the specified record by id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<bool> Delete(Guid id);

        /// <summary>
        /// Delete records matching <param name="predicate"></param>
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        Task<int> DeleteMany(Expression<Func<T, bool>> predicate);
        
        /// <summary>
        /// Deletes all records; careful now :)
        /// </summary>
        /// <returns></returns>
        Task<int> DeleteAll();

        /// <summary>
        /// Creates an index
        /// </summary>
        /// <param name="keySelector"></param>
        /// <param name="unique"></param>
        /// <typeparam name="K"></typeparam>
        /// <returns></returns>
        Task EnsureIndex<K>(Expression<Func<T, K>> keySelector, bool unique = false);
        
    }
}