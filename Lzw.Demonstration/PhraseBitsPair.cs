using System;
using System.Collections.Generic;
using System.Linq;

namespace Lzw.Demonstration;
public readonly record struct PhraseBitsPair(ReadOnlyMemory<char> Phrase, ReadOnlyMemory<bool> Bits) {
    public bool Equals(PhraseBitsPair other, StringComparison comparison = 0) =>
        MemoryExtensions.Equals(Phrase.Span, other.Phrase.Span, comparison) &&
        Bits.Span.SequenceEqual(other.Bits.Span);
}
