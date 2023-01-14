using System;

namespace Lzw;
public sealed class LzwEncodeCompleteActionEntry : LzwEncodeActionEntry {
    public int Encoded { get; }

    public LzwEncodeCompleteActionEntry(int encoded) => Encoded = encoded;
}
