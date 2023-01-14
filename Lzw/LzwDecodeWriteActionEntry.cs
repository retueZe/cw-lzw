using System;

namespace Lzw;
public sealed record LzwDecodeWriteActionEntry(ReadOnlyMemory<char> WrittenPhrase, ReadOnlyMemory<bool> AddedBits, ReadOnlyMemory<char> AddedPhrase) : LzwDecodeActionEntry;
