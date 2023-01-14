namespace Lzw;
public sealed class LzwEncodeReadActionEntry : LzwEncodeActionEntry {
    public char Char { get; }

    public LzwEncodeReadActionEntry(char @char) => Char = @char;
}
