namespace MdcAi.Extensions;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// An immutable, comparable, interoperable array which computes hashcode based on its elements.
///
/// Suitable for any hash table, struct, record, etc. Hashcode is precomputed. Implicit conversion
/// to and from native array is possible.
///
/// Is it fast?
/// - No
/// But does it work?
/// - Almost all of the time 
/// </summary>
public class StableArray<T> : IEnumerable<T>, IEquatable<StableArray<T>>
{
    private readonly T[] _arr;
    private readonly int _hash;

    public StableArray() { _arr = Array.Empty<T>(); }

    public StableArray(T[] arr)
    {
        _arr = arr.ToArray();

        unchecked
        {
            _hash = _arr.Where(i => i != null)
                        .Aggregate(
                            (int)2166136261,
                            (current, item) => (current * 16777619) ^ item.GetHashCode());
        }
    }

    public StableArray(IEnumerable<T> arr)
        : this(arr.ToArray()) { }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_arr).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator StableArray<T>(T[] arr) => arr == null ? null : new StableArray<T>(arr);

    public static implicit operator T[](StableArray<T> arr) => arr?.ToArray();

    public override int GetHashCode() => _hash;

    public override bool Equals(object obj)
    {
        if (obj is StableArray<T> objVal)
            return Equals(objVal);
        return false;
    }

    public bool Equals(StableArray<T> other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return _hash.Equals(other._hash);
    }
}

public static class StableArrayExt
{
    public static StableArray<T> ToStableArray<T>(this IEnumerable<T> source) => new StableArray<T>(source);
}