using System;
using System.Collections.Generic;
using System.Linq;

namespace Lzw;
sealed class MemoryEqualityComparer<T> : IEqualityComparer<ReadOnlyMemory<T>> {
    public static readonly MemoryEqualityComparer<T> Shared = new();

    public IEqualityComparer<T> ItemComparer { get; }

    public MemoryEqualityComparer(IEqualityComparer<T>? itemComparer = null) =>
        ItemComparer = itemComparer ?? EqualityComparer<T>.Default;

    public bool Equals(ReadOnlyMemory<T> left, ReadOnlyMemory<T> right) =>
        left.Span.SequenceEqual(right.Span, ItemComparer);
    public int GetHashCode(ReadOnlyMemory<T> memory) {
        var hashCode = new HashCode();

        foreach (var item in memory.Span)
            hashCode.Add(item is null ? 0 : ItemComparer.GetHashCode(item));

        return hashCode.ToHashCode();
    }
}
