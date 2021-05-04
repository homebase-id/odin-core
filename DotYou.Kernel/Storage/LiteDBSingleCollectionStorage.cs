using DotYou.Types;
using LiteDB;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace DotYou.Kernel.Storage
{
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

            string finalPath = Path.Combine(_dbPath, $"{collectionName}.db");
            logger.LogInformation($"Database path is [{finalPath}]");

            //var cs = new ConnectionString()
            //{
            //    Filename = finalPath,
            //    Connection = ConnectionType.Shared
            //};

            //_db = new LiteDatabase(cs);
            _db = new LiteDatabase(finalPath);
            _collectionName = collectionName;
        }

        public Task<PagedResult<T>> GetList(PageOptions req)
        {
            var col = GetCollection();
            var q = col.Query();
            var total = q.LongCount();
            //q.OrderByDescending()

            var data = q.Limit(req.Size).Offset(req.PageIndex).ToList();
            var result = new PagedResult<T>(req, req.GetTotalPages(total), data);
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
            var limit = req.Size;
            var totalCount = col.Count(predicate);
            var data = col.Find(predicate, skip, limit).ToList();
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
            var id = col.Upsert(item);
            return Task.CompletedTask;
        }

        public Task Delete(Guid id)
        {
            var col = GetCollection();
            col.Delete(id);
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
                _logger.LogInformation($"Disposing of {_collectionName} litedb instance");
                _db.Dispose();
            }
        }
    }
}
