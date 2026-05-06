using System;
using System.Runtime.CompilerServices;

namespace Odin.Core.Storage.Cache;

#nullable enable

//
// Fail fast when callers try to cache a type that does not round-trip through the
// default System.Text.Json serializer. Hits the same class of bugs as the
// QueryBatch NRE: silent corruption from missing constructors / skipped members.
//
// Currently rejects:
//   - ValueTuple<...>: Item1/Item2/... are public fields, which STJ skips by default.
//     Entry serializes as "{}", deserializes back to default(...).
//   - Tuple<...>: Item1/Item2/... are read-only properties with no parameterless ctor
//     and no [JsonConstructor], so STJ cannot materialize them on read.
//   - Anonymous types: read-only properties with positional ctor; STJ cannot construct
//     them, and you cannot name them at the read site anyway.
//
// Use a named record (or class with properties + a parameterless or [JsonConstructor]
// ctor) instead.
//
// Implementation note: a generic static class lets the JIT cache the per-T check
// behind a single static field read on the hot path. The throw fires once per
// offending closed type, in the type initializer, the first time it is touched.
//
internal static class CacheTypeGuard<T>
{
    private static readonly bool _validated = Validate();

    public static void EnsureValid()
    {
        // Touch the static field to force the type initializer (and Validate) to run
        // exactly once per closed generic. Subsequent calls fold to a single field read.
        _ = _validated;
    }

    private static bool Validate()
    {
        var t = typeof(T);

        if (IsValueTuple(t))
        {
            throw new InvalidOperationException(
                $"Caching {t} is not supported: ValueTuple uses public fields which do not " +
                "round-trip through the default System.Text.Json serializer. Wrap the value " +
                "in a named record (e.g. record FooResult(...)) instead.");
        }

        if (IsTuple(t))
        {
            throw new InvalidOperationException(
                $"Caching {t} is not supported: Tuple<> has read-only properties and no " +
                "parameterless constructor, so System.Text.Json cannot deserialize it. Wrap " +
                "the value in a named record (e.g. record FooResult(...)) instead.");
        }

        if (IsAnonymousType(t))
        {
            throw new InvalidOperationException(
                $"Caching {t} is not supported: anonymous types have read-only properties " +
                "and a positional constructor that System.Text.Json cannot bind. Use a named " +
                "record (e.g. record FooResult(...)) instead.");
        }

        return true;
    }

    private static bool IsValueTuple(Type t)
    {
        if (!t.IsGenericType)
        {
            return false;
        }
        var def = t.GetGenericTypeDefinition();
        return def == typeof(ValueTuple<>)
            || def == typeof(ValueTuple<,>)
            || def == typeof(ValueTuple<,,>)
            || def == typeof(ValueTuple<,,,>)
            || def == typeof(ValueTuple<,,,,>)
            || def == typeof(ValueTuple<,,,,,>)
            || def == typeof(ValueTuple<,,,,,,>)
            || def == typeof(ValueTuple<,,,,,,,>);
    }

    private static bool IsTuple(Type t)
    {
        if (!t.IsGenericType)
        {
            return false;
        }
        var def = t.GetGenericTypeDefinition();
        return def == typeof(Tuple<>)
            || def == typeof(Tuple<,>)
            || def == typeof(Tuple<,,>)
            || def == typeof(Tuple<,,,>)
            || def == typeof(Tuple<,,,,>)
            || def == typeof(Tuple<,,,,,>)
            || def == typeof(Tuple<,,,,,,>)
            || def == typeof(Tuple<,,,,,,,>);
    }

    private static bool IsAnonymousType(Type t)
    {
        // Compiler-emitted anonymous types: sealed class, [CompilerGenerated], and
        // a name beginning with "<>" (Roslyn convention). All three together is reliable.
        return t.IsClass
            && t.IsSealed
            && t.Name.StartsWith("<>", StringComparison.Ordinal)
            && t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
    }
}
