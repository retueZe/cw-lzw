using System;

namespace Lzw;
public sealed record LzwDecodeReadActionEntry(ReadOnlyMemory<bool> Bits) : LzwDecodeActionEntry;
