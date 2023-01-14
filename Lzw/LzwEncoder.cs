using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Lzw;
using static Math;
public sealed class LzwEncoder {
    public IImmutableList<char> Charset { get; }
    public int InitialCodeLength { get; }

    public LzwEncoder(IEnumerable<char> charset) {
        charset = charset.Distinct();
        var codeLength = (int)Ceiling(Log2(charset.Count()));

        if (codeLength == 0) codeLength = 1;

        Charset = charset.ToImmutableList();
        InitialCodeLength = codeLength;
    }
    
    public bool TryEncode(IBufferWriter<byte> output, ReadOnlySpan<char> input, out int encoded, IProgress<LzwEncodeActionEntry>? progress = null) {
        encoded = 0;

        if (input.IsEmpty) return true;

        using var _ = PrepareCharset(out var charset);
        char[] phrase = ArrayPool<char>.Shared.Rent(32);
        char[]? newPhraseBuffer = null;

        try {
            var readed = 0;
            var phraseLength = 0;
            phrase[phraseLength++] = input[readed++];
            var writer = new SpanBitWriter(output);
            var codeLength = InitialCodeLength;
            IMemoryOwner<char> previousPhraseOwner;
            Memory<char> addedPhraseMemory;
            Span<char> addedPhrase;
            ulong code, previousCode;
            using (previousPhraseOwner = MemoryPool<char>.Shared.Rent(1)) {
                var previousPhrase = previousPhraseOwner.Memory[..1];
                previousPhrase.Span[0] = Charset[0];
                previousCode = charset[previousPhrase].Code;

                if (readed >= input.Length) {
                    writer.Write(previousCode, codeLength);

                    if (progress is not null) {
                        var entry = new LzwEncodeWriteActionEntry(
                            default, default,
                            previousPhrase.ToArray(), CodeToBits(previousCode, codeLength));
                        progress.Report(entry);
                    }

                    encoded = 1;

                    return true;
                }
            }

            while (readed < input.Length) {
                if (phraseLength >= phrase.Length) {
                    newPhraseBuffer = ArrayPool<char>.Shared.Rent(phrase.Length << 1);
                    phrase.CopyTo(newPhraseBuffer.AsSpan());

                    ArrayPool<char>.Shared.Return(phrase);

                    phrase = newPhraseBuffer;
                    newPhraseBuffer = null;
                }

                phrase[phraseLength++] = input[readed++];

                if (progress is not null) {
                    var entry = new LzwEncodeReadActionEntry(phrase[phraseLength - 1]);
                    progress.Report(entry);
                }
                if (TryGetFromPreparedCharset(charset, phrase.AsMemory(0, phraseLength), out var _)) continue;

                addedPhraseMemory = RentPhrase(out var addedPhraseDisposable, phraseLength);
                addedPhrase = addedPhraseMemory.Span;
                phrase[..phraseLength].CopyTo(addedPhrase);
                code = (ulong)charset.Count;
                charset.Add(addedPhraseMemory, new(code, addedPhraseDisposable));

                using (previousPhraseOwner = MemoryPool<char>.Shared.Rent(phraseLength - 1)) {
                    var previousPhrase = previousPhraseOwner.Memory[..(phraseLength - 1)];
                    addedPhrase[..^1].CopyTo(previousPhrase.Span);
                    previousCode = charset[previousPhrase].Code;
                
                    writer.Write(previousCode, codeLength);
                    
                    phrase[0] = phrase[phraseLength - 1];
                    phraseLength = 1;
                    
                    var previousCodeLength = codeLength;

                    if ((code >> codeLength) != 0) codeLength++;
                    if (progress is not null) {    
                        var entry = new LzwEncodeWriteActionEntry(
                            addedPhrase.ToArray(), CodeToBits(code, codeLength),
                            previousPhrase.ToArray(), CodeToBits(previousCode, previousCodeLength));
                        progress.Report(entry);
                    }
                }
            }

            code = charset[phrase.AsMemory(0, phraseLength)].Code;

            writer.Write(code, codeLength);

            phrase[0] = phrase[phraseLength - 1];
            phraseLength = 1;

            if (progress is not null) {
                var entry = new LzwEncodeWriteActionEntry(
                    default, default,
                    phrase[..phraseLength], CodeToBits(code, codeLength));
                progress.Report(entry);
            }

            encoded = (writer.Written & 0x7) == 0 ? writer.Written >> 3 : (writer.Written >> 3) + 1;

            if (progress is not null) progress.Report(new LzwEncodeCompleteActionEntry(encoded));

            return true;
        } finally {
            ArrayPool<char>.Shared.Return(phrase);

            if (newPhraseBuffer is not null) ArrayPool<char>.Shared.Return(newPhraseBuffer);
        }
    }
    IDisposable PrepareCharset(out IDictionary<ReadOnlyMemory<char>, PreparedCharsetEntry> charset) {
        charset = new Dictionary<ReadOnlyMemory<char>, PreparedCharsetEntry>(PhraseComparer.Shared);

        try {
            foreach (var @char in Charset) {
                var phrase = RentPhrase(out var phraseDisposable, 1);
                phrase.Span[0] = @char;
                charset.Add(phrase, new((ulong)charset.Count, phraseDisposable));
            }

            return new PreparedCharsetDisposable(charset);
        } catch {
            foreach (var entry in charset.Values)
                entry.PhraseDisposable.Dispose();

            throw;
        }
    }
    static Memory<char> RentPhrase(out IDisposable phraseDisposable, int phraseLength) {
        var phraseOwner = MemoryPool<char>.Shared.Rent(phraseLength);
        phraseDisposable = phraseOwner;
        
        return phraseOwner.Memory[..phraseLength];
    }
    static bool TryGetFromPreparedCharset(IDictionary<ReadOnlyMemory<char>, PreparedCharsetEntry> charset, ReadOnlyMemory<char> phrase, out ulong code) {
        code = 0;

        if (!charset.TryGetValue(phrase, out var entry)) return false;

        code = entry.Code;

        return true;
    }
    public static Memory<bool> CodeToBits(ulong code, int codeLength) {
        var bits = new bool[codeLength].AsMemory();
        var bitsSpan = bits.Span;

        for (var i = 0; i < codeLength; i++)
            bitsSpan[i] = ((code >> (codeLength - i - 1)) & 0x1) != 0;

        return bits;
    }
    public LzwDecoder ToDecoder() => new(this);
    
    record struct PreparedCharsetEntry(ulong Code, IDisposable PhraseDisposable);
    sealed class PreparedCharsetDisposable : IDisposable {
        readonly IDictionary<ReadOnlyMemory<char>, PreparedCharsetEntry> _target;

        public PreparedCharsetDisposable(IDictionary<ReadOnlyMemory<char>, PreparedCharsetEntry> target) =>
            _target = target;

        public void Dispose() {
            foreach (var entry in _target.Values)
                entry.PhraseDisposable.Dispose();
        }
    }
    sealed class PhraseComparer : IEqualityComparer<ReadOnlyMemory<char>> {
        public static readonly PhraseComparer Shared = new();
        
        PhraseComparer() {}

        public bool Equals(ReadOnlyMemory<char> left, ReadOnlyMemory<char> right) =>
            MemoryExtensions.Equals(left.Span, right.Span, StringComparison.Ordinal);
        public int GetHashCode(ReadOnlyMemory<char> phrase) =>
            string.GetHashCode(phrase.Span, StringComparison.Ordinal);
    }
    ref struct SpanBitWriter {
        public IBufferWriter<byte> Target { get; }
        public Span<byte> CurrentChunk { get; private set; }
        public int Written { get; private set; } = 0;
        public IProgress<LzwEncodeActionEntry>? Progress { get; init; } = null;

        public SpanBitWriter(IBufferWriter<byte> target) {
            Target = target;
            CurrentChunk = target.GetSpan();
        }

        public void Write(ulong code, int codeLength) {
            var oldBytesWritten = (Written & 0x7) == 0 ? (Written >> 3) : (Written >> 3) + 1;

            while (codeLength > 0) {
                var byteOffset = Written >> 3;
                var bitOffset = Written & 0x7;
                var toWrite = Min(8 - bitOffset, codeLength);

                if (byteOffset >= CurrentChunk.Length - 1 && toWrite <= 0) {
                    CurrentChunk = Target.GetSpan();
                    byteOffset = 0;
                    bitOffset = 0;
                    toWrite = Min(8, codeLength);
                }

                CurrentChunk[byteOffset] |= (byte)(((code >> (codeLength - toWrite)) & ((1UL << toWrite) - 1)) << (8 - bitOffset - toWrite));
                Written += toWrite;
                codeLength -= toWrite;
            }

            var bytesWritten = (Written & 0x7) == 0 ? (Written >> 3) : (Written >> 3) + 1;

            if (bytesWritten > oldBytesWritten)
                Target.Advance(bytesWritten - oldBytesWritten);
        }
    }
}
