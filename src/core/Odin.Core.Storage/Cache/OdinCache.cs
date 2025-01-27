using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

#nullable enable

/// <summary>
/// FusionCache wrapper adding key prefixes and other Odin-specific features.
/// </summary>
public interface IOdinCache
{
	// GET OR SET

    /// <summary>
    /// Get the value of type <typeparamref name="TValue"/> in the cache for the specified <paramref name="key"/>: if not there, the <paramref name="factory"/> will be called and the returned value saved according to the <paramref name="options"/> provided.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the cache.</typeparam>
    /// <param name="key">The cache key which identifies the entry in the cache.</param>
    /// <param name="factory">The function which will be called if the value is not found in the cache.</param>
    /// <param name="failSafeDefaultValue">In case fail-safe is activated and there's no stale data to use, this value will be used instead of throwing an exception.</param>
    /// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
    /// <param name="tags">The optional set of tags related to the entry: this may be used to remove/expire multiple entries at once, by tag.</param>
    /// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
    /// <returns>The value in the cache, either already there or generated using the provided <paramref name="factory"/> .</returns>
    ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory,
        MaybeValue<TValue> failSafeDefaultValue = default,
        FusionCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken token = default);

    /// <summary>
    /// Get the value of type <typeparamref name="TValue"/> in the cache for the specified <paramref name="key"/>: if not there, the <paramref name="factory"/> will be called and the returned value saved according to the <paramref name="options"/> provided.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the cache.</typeparam>
    /// <param name="key">The cache key which identifies the entry in the cache.</param>
    /// <param name="factory">The function which will be called if the value is not found in the cache.</param>
    /// <param name="failSafeDefaultValue">In case fail-safe is activated and there's no stale data to use, this value will be used instead of throwing an exception.</param>
    /// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
    /// <param name="tags">The optional set of tags related to the entry: this may be used to remove/expire multiple entries at once, by tag.</param>
    /// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
    /// <returns>The value in the cache, either already there or generated using the provided <paramref name="factory"/> .</returns>
    TValue GetOrSet<TValue>(
        string key,
        Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory,
        MaybeValue<TValue> failSafeDefaultValue = default,
        FusionCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken token = default);

    /// <summary>
    /// Get the value of type <typeparamref name="TValue"/> in the cache for the specified <paramref name="key"/>: if not there, the <paramref name="defaultValue"/> will be saved according to the <paramref name="options"/> provided.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the cache.</typeparam>
    /// <param name="key">The cache key which identifies the entry in the cache.</param>
    /// <param name="defaultValue">In case the value is not in the cache this value will be saved and returned instead.</param>
    /// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
    /// <param name="tags">The optional set of tags related to the entry: this may be used to remove/expire multiple entries at once, by tag.</param>
    /// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
    ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        TValue defaultValue,
        FusionCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken token = default);

	/// <summary>
	/// Get the value of type <typeparamref name="TValue"/> in the cache for the specified <paramref name="key"/>: if not there, the <paramref name="defaultValue"/> will be saved according to the <paramref name="options"/> provided.
	/// </summary>
	/// <typeparam name="TValue">The type of the value in the cache.</typeparam>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="defaultValue">In case the value is not in the cache this value will be saved and returned instead.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="tags">The optional set of tags related to the entry: this may be used to remove/expire multiple entries at once, by tag.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	TValue GetOrSet<TValue>(
		string key,
		TValue defaultValue,
		FusionCacheEntryOptions? options = null,
		IEnumerable<string>? tags = null,
		CancellationToken token = default);

	// GET OR DEFAULT

	/// <summary>
	/// Get the value of type <typeparamref name="TValue"/> in the cache for the specified <paramref name="key"/>: if not there, the <paramref name="defaultValue"/> will be returned.
	/// </summary>
	/// <typeparam name="TValue">The type of the value in the cache.</typeparam>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="defaultValue">The default value to return if the value for the given <paramref name="key"/> is not in the cache.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	/// <returns>The value in the cache or the <paramref name="defaultValue"/> .</returns>
	ValueTask<TValue?> GetOrDefaultAsync<TValue>(
		string key,
		TValue? defaultValue = default,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default);

	/// <summary>
	/// Get the value of type <typeparamref name="TValue"/> in the cache for the specified <paramref name="key"/>: if not there, the <paramref name="defaultValue"/> will be returned.
	/// </summary>
	/// <typeparam name="TValue">The type of the value in the cache.</typeparam>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="defaultValue">The default value to return if the value for the given <paramref name="key"/> is not in the cache.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	/// <returns>The value in the cache or the <paramref name="defaultValue"/> .</returns>
	TValue? GetOrDefault<TValue>(
		string key,
		TValue? defaultValue = default,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default);

	// TRY GET

	/// <summary>
	/// Try to get the value of type <typeparamref name="TValue"/> in the cache for the specified <paramref name="key"/> and returns a <see cref="MaybeValue{TValue}"/> instance.
	/// </summary>
	/// <typeparam name="TValue">The type of the value in the cache.</typeparam>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	ValueTask<MaybeValue<TValue>> TryGetAsync<TValue>(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default);

	/// <summary>
	/// Try to get the value of type <typeparamref name="TValue"/> in the cache for the specified <paramref name="key"/> and returns a <see cref="MaybeValue{TValue}"/> instance.
	/// </summary>
	/// <typeparam name="TValue">The type of the value in the cache.</typeparam>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	MaybeValue<TValue> TryGet<TValue>(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default);

	// SET

	/// <summary>
	/// Put the <paramref name="value"/> in the cache for the specified <paramref name="key"/>, optionally tagged with the specified <paramref name="tags"/>, with the provided <paramref name="options"/>. If a value is already there it will be overwritten.
	/// </summary>
	/// <typeparam name="TValue">The type of the value in the cache.</typeparam>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="value">The value to put in the cache.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="tags">The optional set of tags related to the entry: this may be used to remove/expire multiple entries at once, by tag.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	/// <returns>A <see cref="ValueTask"/> to await the completion of the operation.</returns>
	ValueTask SetAsync<TValue>(
		string key,
		TValue value,
		FusionCacheEntryOptions? options = null,
		IEnumerable<string>? tags = null,
		CancellationToken token = default);

	/// <summary>
	/// Put the <paramref name="value"/> in the cache for the specified <paramref name="key"/> with the provided <paramref name="options"/>. If a value is already there it will be overwritten.
	/// </summary>
	/// <typeparam name="TValue">The type of the value in the cache.</typeparam>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="value">The value to put in the cache.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="tags">The optional set of tags related to the entry: this may be used to remove/expire multiple entries at once, by tag.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	void Set<TValue>(
		string key,
		TValue value,
		FusionCacheEntryOptions? options = null,
		IEnumerable<string>? tags = null,
		CancellationToken token = default);

	// REMOVE

	/// <summary>
	/// Removes the value in the cache for the specified <paramref name="key"/>.
	/// </summary>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	/// <returns>A <see cref="ValueTask"/> to await the completion of the operation.</returns>
	ValueTask RemoveAsync(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default);

	/// <summary>
	/// Removes the value in the cache for the specified <paramref name="key"/>.
	/// </summary>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	void Remove(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default);

	// EXPIRE

	/// <summary>
	/// Expires the cache entry for the specified <paramref name="key"/>: that can mean an Expire (if fail-safe was enabled when saving the entry) or a Remove (if fail-safe was NOT enabled when saving the entry), all automatically.
	/// <br/>
	/// <br/>
	/// In the distributed cache (if any), the entry will always be effectively removed.
	/// </summary>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	/// <returns>A <see cref="ValueTask"/> to await the completion of the operation.</returns>
	ValueTask ExpireAsync(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default);

	/// <summary>
	/// Expires the cache entry for the specified <paramref name="key"/>: that can mean an Expire (if fail-safe was enabled when saving the entry) or a Remove (if fail-safe was NOT enabled when saving the entry), all automatically.
	/// <br/>
	/// <br/>
	/// In the distributed cache (if any), the entry will always be effectively removed.
	/// </summary>
	/// <param name="key">The cache key which identifies the entry in the cache.</param>
	/// <param name="options">The options to adhere during this operation. If null is passed, <see cref="DefaultEntryOptions"/> will be used.</param>
	/// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
	void Expire(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default);

	// A GAZILLION EXTENSION METHODS (from the various FusionCacheExtMethods classes)

	ValueTask<TValue> GetOrSetAsync<TValue>(string key, TValue defaultValue, TimeSpan duration, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, TValue defaultValue, TimeSpan duration,
		IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, TValue defaultValue,
		Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, TValue defaultValue,
		Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null,
		CancellationToken token = default);
	ValueTask<TValue?> GetOrDefaultAsync<TValue>(string key, Action<FusionCacheEntryOptions> setupAction,
		TValue? defaultValue = default, CancellationToken token = default);
	ValueTask<TValue?> GetOrDefaultAsync<TValue>(string key, TValue? defaultValue,
		Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default);
	ValueTask<MaybeValue<TValue>> TryGetAsync<TValue>(string key, Action<FusionCacheEntryOptions> setupAction,
		CancellationToken token = default);
	ValueTask SetAsync<TValue>(string key, TValue value, TimeSpan duration, CancellationToken token);
	ValueTask SetAsync<TValue>(string key, TValue value, TimeSpan duration, IEnumerable<string>? tags = null,
		CancellationToken token = default);
	ValueTask SetAsync<TValue>(string key, TValue value, Action<FusionCacheEntryOptions> setupAction,
		CancellationToken token);
	ValueTask SetAsync<TValue>(string key, TValue value, Action<FusionCacheEntryOptions> setupAction,
		IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask SetAsync<TValue>(string key, TValue value, FusionCacheEntryOptions? options, CancellationToken token);
	ValueTask RemoveAsync(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default);
	ValueTask ExpireAsync(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, TValue defaultValue, TimeSpan duration, CancellationToken token);
	TValue GetOrSet<TValue>(string key, TValue defaultValue, TimeSpan duration, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, TValue defaultValue, Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	TValue GetOrSet<TValue>(string key, TValue defaultValue, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue? GetOrDefault<TValue>(string key, Action<FusionCacheEntryOptions> setupAction, TValue? defaultValue = default, CancellationToken token = default);
	TValue? GetOrDefault<TValue>(string key, TValue? defaultValue, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default);
	MaybeValue<TValue> TryGet<TValue>(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default);
	void Set<TValue>(string key, TValue value, TimeSpan duration, CancellationToken token);
	void Set<TValue>(string key, TValue value, TimeSpan duration, IEnumerable<string>? tags = null, CancellationToken token = default);
	void Set<TValue>(string key, TValue value, Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	void Set<TValue>(string key, TValue value, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null, CancellationToken token = default);
	void Set<TValue>(string key, TValue value, FusionCacheEntryOptions? options, CancellationToken token);
	void Remove(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default);
	void Expire(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, FusionCacheEntryOptions? options, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, FusionCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration, IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, FusionCacheEntryOptions? options, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, FusionCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, TimeSpan duration, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, TimeSpan duration, IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, FusionCacheEntryOptions? options, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, FusionCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, FusionCacheEntryOptions? options, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, FusionCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, TimeSpan duration, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, TimeSpan duration, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration, IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, FusionCacheEntryOptions? options, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, FusionCacheEntryOptions? options, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, FusionCacheEntryOptions? options, IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, TimeSpan duration, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, TimeSpan duration, IEnumerable<string>? tags = null, CancellationToken token = default);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, FusionCacheEntryOptions? options, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, FusionCacheEntryOptions? options, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, FusionCacheEntryOptions? options, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, TimeSpan duration, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, TimeSpan duration, IEnumerable<string>? tags = null, CancellationToken token = default);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, Action<FusionCacheEntryOptions> setupAction, CancellationToken token);
	TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null, CancellationToken token = default);
}

//

/// <summary>
/// FusionCache wrapper adding key prefixes and other Odin-specific features.
/// </summary>
public class OdinCache(OdinCacheKeyPrefix prefix, IFusionCache cache) : IOdinCache
{
	public ValueTask<TValue> GetOrSetAsync<TValue>(
		string key,
		Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory,
		MaybeValue<TValue> failSafeDefaultValue = default,
		FusionCacheEntryOptions? options = null,
		IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
            prefix + key,
            factory,
            failSafeDefaultValue,
            options,
            tags,
            token);
	}

	public TValue GetOrSet<TValue>(
		string key,
		Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory,
		MaybeValue<TValue> failSafeDefaultValue = default,
		FusionCacheEntryOptions? options = null,
		IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			options,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(
		string key,
		TValue defaultValue, FusionCacheEntryOptions? options = null,
		IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			defaultValue,
			options,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(
		string key,
		TValue defaultValue,
		FusionCacheEntryOptions? options = null,
		IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			defaultValue,
			options,
			tags,
			token);
	}

	public ValueTask<TValue?> GetOrDefaultAsync<TValue>(
		string key,
		TValue? defaultValue = default,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default)
	{
		return cache.GetOrDefaultAsync(
			prefix + key,
			defaultValue,
			options,
			token);
	}

	public TValue? GetOrDefault<TValue>(
		string key,
		TValue? defaultValue = default,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default)
	{
		return cache.GetOrDefault(
			prefix + key,
			defaultValue,
			options,
			token);
	}

	public ValueTask<MaybeValue<TValue>> TryGetAsync<TValue>(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default)
	{
		return cache.TryGetAsync<TValue>(
			prefix + key,
			options,
			token);
	}

	public MaybeValue<TValue> TryGet<TValue>(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default)
	{
		return cache.TryGet<TValue>(
			prefix + key,
			options,
			token);
	}

	public ValueTask SetAsync<TValue>(
		string key,
		TValue value,
		FusionCacheEntryOptions? options = null,
		IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.SetAsync(
			prefix + key,
			value,
			options,
			tags,
			token);
	}

	public void Set<TValue>(
		string key,
		TValue value,
		FusionCacheEntryOptions? options = null,
		IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		cache.Set(
			prefix + key,
			value,
			options,
			tags,
			token);
	}

	public ValueTask RemoveAsync(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default)
	{
		return cache.RemoveAsync(
			prefix + key,
			options,
			token);
	}

	public void Remove(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default)
	{
		cache.Remove(
			prefix + key,
			options,
			token);
	}

	public ValueTask ExpireAsync(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default)
	{
		return cache.ExpireAsync(
			prefix + key,
			options,
			token);
	}

	public void Expire(
		string key,
		FusionCacheEntryOptions? options = null,
		CancellationToken token = default)
	{
		cache.Expire(
			prefix + key,
			options,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, TValue defaultValue, TimeSpan duration, CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			defaultValue,
			duration,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, TValue defaultValue, TimeSpan duration, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			defaultValue,
			duration,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, TValue defaultValue, Action<FusionCacheEntryOptions> setupAction, CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			defaultValue,
			setupAction,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, TValue defaultValue, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			defaultValue,
			setupAction,
			tags,
			token);
	}

	public ValueTask<TValue?> GetOrDefaultAsync<TValue>(string key, Action<FusionCacheEntryOptions> setupAction, TValue? defaultValue = default,
		CancellationToken token = default)
	{
		return cache.GetOrDefaultAsync(
			prefix + key,
			setupAction,
			defaultValue,
			token);
	}

	public ValueTask<TValue?> GetOrDefaultAsync<TValue>(string key, TValue? defaultValue, Action<FusionCacheEntryOptions> setupAction,
		CancellationToken token = default)
	{
		return cache.GetOrDefaultAsync(
			prefix + key,
			defaultValue,
			setupAction,
			token);
	}

	public ValueTask<MaybeValue<TValue>> TryGetAsync<TValue>(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default)
	{
		return cache.TryGetAsync<TValue>(
			prefix + key,
			setupAction,
			token);
	}

	public ValueTask SetAsync<TValue>(string key, TValue value, TimeSpan duration, CancellationToken token)
	{
		return cache.SetAsync(
			prefix + key,
			value,
			duration,
			token);
	}

	public ValueTask SetAsync<TValue>(string key, TValue value, TimeSpan duration, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.SetAsync(
			prefix + key,
			value,
			duration,
			tags,
			token);
	}

	public ValueTask SetAsync<TValue>(string key, TValue value, Action<FusionCacheEntryOptions> setupAction, CancellationToken token)
	{
		return cache.SetAsync(
			prefix + key,
			value,
			setupAction,
			token);
	}

	public ValueTask SetAsync<TValue>(string key, TValue value, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.SetAsync(
			prefix + key,
			value,
			setupAction,
			tags,
			token);
	}

	public ValueTask SetAsync<TValue>(string key, TValue value, FusionCacheEntryOptions? options, CancellationToken token)
	{
		return cache.SetAsync(
			prefix + key,
			value,
			options,
			token);
	}

	public ValueTask RemoveAsync(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default)
	{
		return cache.RemoveAsync(
			prefix + key,
			setupAction,
			token);
	}

	public ValueTask ExpireAsync(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default)
	{
		return cache.ExpireAsync(
			prefix + key,
			setupAction,
			token);
	}

	public TValue GetOrSet<TValue>(string key, TValue defaultValue, TimeSpan duration, CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			defaultValue,
			duration,
			token);
	}

	public TValue GetOrSet<TValue>(string key, TValue defaultValue, TimeSpan duration, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			defaultValue,
			duration,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, TValue defaultValue, Action<FusionCacheEntryOptions> setupAction, CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			defaultValue,
			setupAction,
			token);
	}

	public TValue GetOrSet<TValue>(string key, TValue defaultValue, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			defaultValue,
			setupAction,
			tags,
			token);
	}

	public TValue? GetOrDefault<TValue>(string key, Action<FusionCacheEntryOptions> setupAction, TValue? defaultValue = default,
		CancellationToken token = default)
	{
		return cache.GetOrDefault(
			prefix + key,
			setupAction,
			defaultValue,
			token);
	}

	public TValue? GetOrDefault<TValue>(string key, TValue? defaultValue, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default)
	{
		return cache.GetOrDefault(
			prefix + key,
			defaultValue,
			setupAction,
			token);
	}

	public MaybeValue<TValue> TryGet<TValue>(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default)
	{
		return cache.TryGet<TValue>(
			prefix + key,
			setupAction,
			token);
	}

	public void Set<TValue>(string key, TValue value, TimeSpan duration, CancellationToken token)
	{
		cache.Set(
			prefix + key,
			value,
			duration,
			token);
	}

	public void Set<TValue>(string key, TValue value, TimeSpan duration, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		cache.Set(
			prefix + key,
			value,
			duration,
			tags,
			token);
	}

	public void Set<TValue>(string key, TValue value, Action<FusionCacheEntryOptions> setupAction, CancellationToken token)
	{
		cache.Set(
			prefix + key,
			value,
			setupAction,
			token);
	}

	public void Set<TValue>(string key, TValue value, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		cache.Set(
			prefix + key,
			value,
			setupAction,
			tags,
			token);
	}

	public void Set<TValue>(string key, TValue value, FusionCacheEntryOptions? options, CancellationToken token)
	{
		cache.Set(
			prefix + key,
			value,
			options,
			token);
	}

	public void Remove(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default)
	{
		cache.Remove(
			prefix + key,
			setupAction,
			token);
	}

	public void Expire(string key, Action<FusionCacheEntryOptions> setupAction, CancellationToken token = default)
	{
		cache.Expire(
			prefix + key,
			setupAction,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue,
		FusionCacheEntryOptions? options, CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			options,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue,
		FusionCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			options,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration,
		CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			duration,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration,
		IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			duration,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction,
		CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			setupAction,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction,
		IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			setupAction,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, FusionCacheEntryOptions? options, CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			options,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, FusionCacheEntryOptions? options = null,
		IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSetAsync<TValue>(
			prefix + key,
			factory,
			options,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, TimeSpan duration, CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			duration,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, TimeSpan duration, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			duration,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, Action<FusionCacheEntryOptions> setupAction, CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			setupAction,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<CancellationToken, Task<TValue>> factory, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			setupAction,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, FusionCacheEntryOptions? options,
		CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			options,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue,
		FusionCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			options,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration,
		CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			duration,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration,
		IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			duration,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction,
		CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			setupAction,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction,
		IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			setupAction,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, FusionCacheEntryOptions? options, CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			options,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, FusionCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSet<TValue>(
			prefix + key,
			factory,
			options,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, TimeSpan duration, CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			duration,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, TimeSpan duration, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			duration,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, Action<FusionCacheEntryOptions> setupAction, CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			setupAction,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<CancellationToken, TValue> factory, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			setupAction,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration,
		CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			duration,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration,
		IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			duration,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction,
		CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			setupAction,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction,
		IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			setupAction,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, MaybeValue<TValue> failSafeDefaultValue,
		FusionCacheEntryOptions? options, CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			failSafeDefaultValue,
			options,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, FusionCacheEntryOptions? options, CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			options,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, FusionCacheEntryOptions? options, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSetAsync<TValue>(
			prefix + key,
			factory,
			options,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, TimeSpan duration, CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			duration,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, TimeSpan duration, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			duration,
			tags,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, Action<FusionCacheEntryOptions> setupAction, CancellationToken token)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			setupAction,
			token);
	}

	public ValueTask<TValue> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue>> factory, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSetAsync(
			prefix + key,
			factory,
			setupAction,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration,
		CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			duration,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, TimeSpan duration,
		IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			duration,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction,
		CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			setupAction,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, Action<FusionCacheEntryOptions> setupAction,
		IEnumerable<string>? tags = null, CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			setupAction,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, MaybeValue<TValue> failSafeDefaultValue, FusionCacheEntryOptions? options,
		CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			failSafeDefaultValue,
			options,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, FusionCacheEntryOptions? options, CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			options,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, FusionCacheEntryOptions? options, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSet<TValue>(
			prefix + key,
			factory,
			options,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, TimeSpan duration, CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			duration,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, TimeSpan duration, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			duration,
			tags,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, Action<FusionCacheEntryOptions> setupAction, CancellationToken token)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			setupAction,
			token);
	}

	public TValue GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue> factory, Action<FusionCacheEntryOptions> setupAction, IEnumerable<string>? tags = null,
		CancellationToken token = default)
	{
		return cache.GetOrSet(
			prefix + key,
			factory,
			setupAction,
			tags,
			token);
	}
}
