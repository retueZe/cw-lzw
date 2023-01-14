using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Lzw;
using static Math;
public sealed class LzwDecoder {
    public IImmutableList<char> Charset { get; }
    public int InitialCodeLength { get; }

    internal LzwDecoder(LzwEncoder encoder) {
        Charset = encoder.Charset;
        InitialCodeLength = encoder.InitialCodeLength;
    }

    public bool TryDecode(IBufferWriter<char> output, ReadOnlySpan<byte> input, out int decoded, IProgress<LzwDecodeActionEntry>? progress = null) {
        decoded = 0;
        var bitReader = new SpanBitReader(input) {
            Progress = progress
        };
        var codeLength = InitialCodeLength;
        using var _ = PrepareCharset(out var charset);
        var phraseLength = 0;
        char[]? newPhrase = null;
        char[] phrase = ArrayPool<char>.Shared.Rent(32);
        Span<char> chunk = output.GetSpan();
        int chunkLength = 0;
        
        try {
            if (!bitReader.TryRead(out var code, codeLength)) return true;
            if (!TryGetFromPreparedCharset(charset, code, out var chars)) return false;

            chunk[chunkLength++] = phrase[0];
            phrase[0] = chars[0];
            phraseLength = 1;
            
            if ((charset.Count >> codeLength) > 0) codeLength++;
            if (progress is not null) {
                var entry = new LzwDecodeWriteActionEntry(phrase[..1], default, default);
                progress.Report(entry);
            }

            while (bitReader.TryRead(out code, codeLength)) {
                if (!TryGetFromPreparedCharset(charset, code, out chars)) return false;
                if (chunk.Length - chunkLength < chars.Length) {
                    chars[..(chunk.Length - chunkLength)].CopyTo(chunk[chunkLength..]);
                    chunk = output.GetSpan(chars.Length - chunk.Length - chunkLength);
                    chunkLength = 0;
                    chars[(chunk.Length - chunkLength)..].CopyTo(chunk);
                } else
                    chars.CopyTo(chunk[chunkLength..]);

                var requriedPhraseCapacity = Max(phraseLength + 1, chars.Length);

                if (phrase.Length < requriedPhraseCapacity) {
                    var newPhraseCapacity = Max(phrase.Length << 1, requriedPhraseCapacity);
                    newPhrase = ArrayPool<char>.Shared.Rent(newPhraseCapacity);
                    phrase.AsSpan().CopyTo(newPhrase);
                    ArrayPool<char>.Shared.Return(phrase);
                    phrase = newPhrase;
                    newPhrase = null;
                }

                phrase[phraseLength++] = chars[0];
                var addedPhraseMemory = RentPhrase(out var addedPhraseDisposable, phraseLength);
                var addedPhrase = addedPhraseMemory.Span;
                phrase.AsSpan(0, phraseLength).CopyTo(addedPhrase);
                charset.Add((ulong)charset.Count, new(addedPhraseMemory, addedPhraseDisposable));
                chars.CopyTo(phrase);
                phraseLength = chars.Length;

                if ((charset.Count >> codeLength) > 0) codeLength++;
                if (progress is not null) {
                    var entry = new LzwDecodeWriteActionEntry(chars.ToArray(), CodeToBits((ulong)charset.Count - 1, codeLength), addedPhrase.ToArray());
                    progress.Report(entry);
                }
            }

            if (progress is not null) {
                var entry = new LzwDecodeCompleteActionEntry(decoded);
                progress.Report(entry);
            }

            return true;
        } finally {
            ArrayPool<char>.Shared.Return(phrase);

            if (newPhrase is not null) ArrayPool<char>.Shared.Return(newPhrase);
        }
    }
    IDisposable PrepareCharset(out IDictionary<ulong, PreparedCharsetEntry> charset) {
        charset = new Dictionary<ulong, PreparedCharsetEntry>();

        try {
            foreach (var @char in Charset) {
                var phrase = RentPhrase(out var phraseDisposable, 1);
                phrase.Span[0] = @char;
                charset.Add((ulong)charset.Count, new(phrase, phraseDisposable));
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
    static bool TryGetFromPreparedCharset(IDictionary<ulong, PreparedCharsetEntry> charset, ulong code, out ReadOnlySpan<char> phrase) {
        phrase = default;

        if (!charset.TryGetValue(code, out var entry)) return false;

        phrase = entry.Phrase.Span;

        return true;
    }
    public static Memory<bool> CodeToBits(ulong code, int codeLength) {
        var bits = new bool[codeLength].AsMemory();
        var bitsSpan = bits.Span;

        for (var i = 0; i < codeLength; i++)
            bitsSpan[i] = ((code >> (codeLength - i - 1)) & 0x1) != 0;

        return bits;
    }
    
    record struct PreparedCharsetEntry(ReadOnlyMemory<char> Phrase, IDisposable PhraseDisposable);
    sealed class PreparedCharsetDisposable : IDisposable {
        readonly IDictionary<ulong, PreparedCharsetEntry> _target;

        public PreparedCharsetDisposable(IDictionary<ulong, PreparedCharsetEntry> target) =>
            _target = target;

        public void Dispose() {
            foreach (var entry in _target.Values)
                entry.PhraseDisposable.Dispose();
        }
    }
    ref struct SpanBitReader {
        public ReadOnlySpan<byte> Source { get; }
        public int Readed { get; private set; } = 0;
        public IProgress<LzwDecodeActionEntry>? Progress { get; init; } = null;

        public SpanBitReader(ReadOnlySpan<byte> source) {
            Source = source;
        }

        public bool TryRead(out ulong code, int codeLength) {
            code = 0;

            if ((Source.Length << 3) - Readed < codeLength) return false;
            
            for (var remainder = codeLength; remainder > 0;) {
                var bufferLength = 8 - (Readed & 0x7);
                bufferLength = Min(bufferLength, remainder);
                // bufferLength - X count
                // Readed & 0x7 - Y count
                // YYYXXXXZ -> 0000XXXX
                var buffer = (byte)((Source[Readed >> 3] >> (8 - bufferLength - (Readed & 0x7))) & ((1 << bufferLength) - 1));
                code = (code << bufferLength) | buffer;
                remainder -= bufferLength;
                Readed += bufferLength;
            }
            
            if (Progress is not null) {
                var entry = new LzwDecodeReadActionEntry(CodeToBits(code, codeLength));
                Progress.Report(entry);
            }
            
            return true;
        }
    }
}
