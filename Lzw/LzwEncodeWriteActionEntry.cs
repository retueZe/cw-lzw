using System;
using System.Collections;
using System.Collections.Generic;

namespace Lzw;
public sealed class LzwEncodeWriteActionEntry : LzwEncodeActionEntry {
    public ReadOnlyMemory<char> AddedPhrase { get; }
    public ReadOnlyMemory<bool> AddedBits { get; }
    public ReadOnlyMemory<char> WrittenPhrase { get; }
    public ReadOnlyMemory<bool> WrittenBits { get; }

    public LzwEncodeWriteActionEntry(ReadOnlyMemory<char> addedPhrase, ReadOnlyMemory<bool> addedBits, ReadOnlyMemory<char> writtenPhrase, ReadOnlyMemory<bool> writtenBits) {
        AddedPhrase = addedPhrase;
        AddedBits = addedBits;
        WrittenPhrase = writtenPhrase;
        WrittenBits = writtenBits;
    }
}
