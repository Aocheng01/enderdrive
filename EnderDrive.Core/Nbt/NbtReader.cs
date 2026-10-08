using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EnderDrive.Core.Nbt;

/// <summary>
/// Lector del formato NBT de Minecraft Java (big-endian).
/// Referencia: https://minecraft.wiki/w/NBT_format
/// </summary>
public sealed class NbtReader
{
    private const int MaxDepth = 512;

    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[8];

    private NbtReader(Stream stream) => _stream = stream;

    /// <summary>Lee un archivo NBT (comprimido con gzip o sin comprimir) y devuelve su compuesto raíz.</summary>
    public static NbtCompound ReadFile(string path)
    {
        using var file = File.OpenRead(path);
        using var stream = IsGzip(file) ? new GZipStream(file, CompressionMode.Decompress) : (Stream)file;
        return Read(new BufferedStream(stream));
    }

    public static NbtCompound Read(Stream stream)
    {
        var reader = new NbtReader(stream);

        if (reader.ReadUInt8() != NbtTagType.Compound)
            throw new InvalidDataException("El archivo NBT no empieza por un TAG_Compound.");

        reader.ReadString(); // nombre de la raíz (normalmente vacío)
        return reader.ReadCompound(depth: 0);
    }

    private static bool IsGzip(FileStream file)
    {
        Span<byte> header = stackalloc byte[2];
        var isGzip = file.Read(header) == 2 && header[0] == 0x1F && header[1] == 0x8B;
        file.Position = 0;
        return isGzip;
    }

    private object ReadPayload(byte type, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidDataException("Anidamiento NBT demasiado profundo.");

        return type switch
        {
            NbtTagType.Byte => (sbyte)ReadUInt8(),
            NbtTagType.Short => ReadInt16(),
            NbtTagType.Int => ReadInt32(),
            NbtTagType.Long => ReadInt64(),
            NbtTagType.Float => BitConverter.Int32BitsToSingle(ReadInt32()),
            NbtTagType.Double => BitConverter.Int64BitsToDouble(ReadInt64()),
            NbtTagType.ByteArray => ReadBytes(ReadLength()),
            NbtTagType.String => ReadString(),
            NbtTagType.List => ReadList(depth),
            NbtTagType.Compound => ReadCompound(depth),
            NbtTagType.IntArray => ReadArray(ReadLength(), ReadInt32),
            NbtTagType.LongArray => ReadArray(ReadLength(), ReadInt64),
            _ => throw new InvalidDataException($"Tipo de etiqueta NBT desconocido: {type}."),
        };
    }

    private NbtCompound ReadCompound(int depth)
    {
        var compound = new NbtCompound();

        while (true)
        {
            var type = ReadUInt8();
            if (type == NbtTagType.End)
                return compound;

            var name = ReadString();
            compound.Add(name, ReadPayload(type, depth + 1));
        }
    }

    private List<object> ReadList(int depth)
    {
        var elementType = ReadUInt8();
        var count = ReadLength();

        var list = new List<object>(Math.Min(count, 1024));
        for (var i = 0; i < count; i++)
            list.Add(ReadPayload(elementType, depth + 1));

        return list;
    }

    private string ReadString()
    {
        var length = (ushort)ReadInt16();
        // NBT usa "Modified UTF-8"; para texto normal es idéntico a UTF-8
        return Encoding.UTF8.GetString(ReadBytes(length));
    }

    private int ReadLength()
    {
        var length = ReadInt32();
        if (length < 0)
            throw new InvalidDataException("Longitud NBT negativa.");
        return length;
    }

    private T[] ReadArray<T>(int length, Func<T> readElement)
    {
        var array = new T[length];
        for (var i = 0; i < length; i++)
            array[i] = readElement();
        return array;
    }

    private byte ReadUInt8()
    {
        var value = _stream.ReadByte();
        if (value < 0)
            throw new EndOfStreamException();
        return (byte)value;
    }

    private short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(Fill(2));

    private int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(Fill(4));

    private long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(Fill(8));

    private Span<byte> Fill(int count)
    {
        var span = _buffer.AsSpan(0, count);
        _stream.ReadExactly(span);
        return span;
    }

    private byte[] ReadBytes(int count)
    {
        var bytes = new byte[count];
        _stream.ReadExactly(bytes);
        return bytes;
    }
}

internal static class NbtTagType
{
    public const byte End = 0;
    public const byte Byte = 1;
    public const byte Short = 2;
    public const byte Int = 3;
    public const byte Long = 4;
    public const byte Float = 5;
    public const byte Double = 6;
    public const byte ByteArray = 7;
    public const byte String = 8;
    public const byte List = 9;
    public const byte Compound = 10;
    public const byte IntArray = 11;
    public const byte LongArray = 12;
}
