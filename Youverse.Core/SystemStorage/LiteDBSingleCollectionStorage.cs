using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.Logging;
using Youverse.Core.Util;

namespace Youverse.Core.SystemStorage
{
    //TODO: need to add support for unique keys in the storage

    /// <summary>
    /// Generic class for storing an object in a single collection.  The database filename will be the collection.
    /// </summary>
    /// <typeparam name="T">The class you want to store and retrieve.</typeparam>
    /// <remarks>This is sealed because it should serve the very-fixed and opinionated purpose of working 
    /// with a single collection LiteDB.  If you need features outside of that, build a customized 
    /// storage for your needs</remarks>
    public sealed class LiteDBSingleCollectionStorage<T> : IStorage<T>
    {
        readonly string _dbPath;
        readonly string _collectionName;

        ILogger _logger;
        LiteDatabase _db;

        public LiteDBSingleCollectionStorage(ILogger logger, string dbPath, string collectionName)
        {
            _logger = logger;
            _dbPath = dbPath;

            if (!Directory.Exists(dbPath))
            {
                _logger.LogInformation($"Creating path at [{_dbPath}]");
                Directory.CreateDirectory(_dbPath);
            }

            string finalPath = PathUtil.Combine(_dbPath, $"{collectionName}.db");
            logger.LogDebug($"Database path is [{finalPath}]");

            //_db = new LiteDatabase(finalPath);
            _db = new LiteDatabase(new ConnectionString
            {
                Filename = finalPath,
                Connection = ConnectionType.Shared,
                ReadOnly = false
            });
            _collectionName = collectionName;
        }

        public Task<PagedResult<T>> GetList(PageOptions req)
        {
            var col = GetCollection();
            var q = col.Query();
            var total = q.LongCount();

            var data = q.Limit(req.PageSize).Offset(req.PageIndex).ToList();
            var result = new PagedResult<T>(req, req.GetTotalPages(total), data);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Retrieves a sorted list
        /// </summary>
        /// <param name="req"></param>
        /// <param name="sortDirection"></param>
        /// <param name="keySelector"></param>
        /// <typeparam name="TK"></typeparam>
        /// <returns></returns>
        public Task<PagedResult<T>> GetList<TK>(PageOptions req, ListSortDirection sortDirection, Expression<Func<T, TK>> keySelector)
        {
            var col = GetCollection();
            var q = col.Query();
            var total = q.LongCount();

            q = (sortDirection == ListSortDirection.Ascending) ? q.OrderBy(keySelector) : q.OrderByDescending(keySelector);

            var data = q.Limit(req.PageSize).Offset(req.PageIndex).ToList();
            var result = new PagedResult<T>(req, req.GetTotalPages(total), data);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Finds the first record matching the predicate; otherwise null.
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="req"></param>
        /// <returns></returns>
        public Task<T> FindOne(Expression<Func<T, bool>> predicate)
        {
            var col = GetCollection();
            var result = col.FindOne(predicate);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Finds all records of T using the given predicate.  This is passed directly to LiteDB.
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="req"></param>
        /// <returns></returns>
        public Task<PagedResult<T>> Find(Expression<Func<T, bool>> predicate, PageOptions req)
        {
            var col = GetCollection();

            var skip = req.GetSkipCount();
            var limit = req.PageSize;
            var totalCount = col.Count(predicate);
            var rdata = col.Find(predicate, skip, limit);
            var data = rdata.ToList();
            var result = new PagedResult<T>(req, req.GetTotalPages(totalCount), data);

            return Task.FromResult(result);
        }

        /// <summary>
        /// Finds all records of T using the given predicate and sorting parameters.  This is passed directly to LiteDB.
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="req"></param>
        /// <param name="sortDirection"></param>
        /// <param name="sortKeySelector"></param>
        /// <returns></returns>
        public Task<PagedResult<T>> Find<TK>(Expression<Func<T, bool>> predicate, ListSortDirection sortDirection, Expression<Func<T, TK>> sortKeySelector, PageOptions req)
        {
            var col = GetCollection();

            var skip = req.GetSkipCount();
            var limit = req.PageSize;
            var totalCount = col.Count(predicate);

            var query = sortDirection == ListSortDirection.Ascending ? col.Query().Where(predicate).OrderBy(sortKeySelector) : col.Query().Where(predicate).OrderByDescending(sortKeySelector);

            var resultData = query.Skip(skip).Limit(limit).ToEnumerable();
            var data = resultData.ToList();
            var result = new PagedResult<T>(req, req.GetTotalPages(totalCount), data);

            return Task.FromResult(result);
        }

        public Task<T> Get(Guid id)
        {
            var col = GetCollection();
            var result = col.FindById(id);
            return Task.FromResult(result);
        }

        public Task Save(T item)
        {
            var col = GetCollection();
            try
            {
                var id = col.Upsert(item);
            }
            catch (LiteDB.LiteException lex)
            {
                if (lex.ErrorCode == 110)
                {
                    throw new UniqueIndexException(lex.Message);
                }

                throw;
            }

            return Task.CompletedTask;
        }

        public Task<bool> Delete(Guid id)
        {
            var col = GetCollection();
            var success = col.Delete(id);
            return Task.FromResult(success);
        }

        public Task<int> DeleteMany(Expression<Func<T, bool>> predicate)
        {
            var col = GetCollection();
            var count = col.DeleteMany(predicate);
            return Task.FromResult(count);
        }

        public Task<int> DeleteAll()
        {
            var col = GetCollection();
            int count = col.DeleteAll();
            return Task.FromResult(count);
        }

        public Task EnsureIndex<K>(Expression<Func<T, K>> keySelector, bool unique = false)
        {
            var col = GetCollection();
            col.EnsureIndex(keySelector, unique);
            return Task.CompletedTask;
        }

        private ILiteCollection<T> GetCollection()
        {
            var collection = _db.GetCollection<T>(_collectionName);
            return collection;
        }

        public void Dispose()
        {
            if (_db != null)
            {
                _logger.LogDebug($"Disposing of {_collectionName} litedb instance");
                _db.Dispose();
            }
        }
    }
}