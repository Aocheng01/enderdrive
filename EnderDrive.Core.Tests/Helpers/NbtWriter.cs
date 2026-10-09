using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EnderDrive.Core.Tests.Helpers;

/// <summary>
/// Escritor NBT mínimo, solo para los tests: nos permite fabricar level.dat de cualquier
/// versión de Minecraft sin depender de mundos reales.
/// El tipo de cada etiqueta se deduce del tipo de C#:
/// sbyte→Byte, short→Short, int→Int, long→Long, float→Float, double→Double, string→String,
/// byte[]→ByteArray, int[]→IntArray, long[]→LongArray, List&lt;object&gt;→List, Nbt→Compound.
/// </summary>
public sealed class Nbt : Dictionary<string, object>
{
    public static void WriteFile(string path, Nbt root, bool gzip = true)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var stream = gzip ? new GZipStream(file, CompressionLevel.Fastest) : (Stream)file;
        Write(stream, root);
    }

    public static void Write(Stream stream, Nbt root)
    {
        stream.WriteByte(10);
        WriteString(stream, "");
        WritePayload(stream, root);
    }

    private static byte TypeOf(object value) => value switch
    {
        sbyte => 1,
        short => 2,
        int => 3,
        long => 4,
        float => 5,
        double => 6,
        byte[] => 7,
        string => 8,
        List<object> => 9,
        Nbt => 10,
        int[] => 11,
        long[] => 12,
        _ => throw new ArgumentException($"Tipo no soportado: {value.GetType()}"),
    };

    private static void WritePayload(Stream s, object value)
    {
        Span<byte> buffer = stackalloc byte[8];
        switch (value)
        {
            case sbyte v: s.WriteByte((byte)v); break;
            case short v: BinaryPrimitives.WriteInt16BigEndian(buffer, v); s.Write(buffer[..2]); break;
            case int v: BinaryPrimitives.WriteInt32BigEndian(buffer, v); s.Write(buffer[..4]); break;
            case long v: BinaryPrimitives.WriteInt64BigEndian(buffer, v); s.Write(buffer[..8]); break;
            case float v: WritePayload(s, BitConverter.SingleToInt32Bits(v)); break;
            case double v: WritePayload(s, BitConverter.DoubleToInt64Bits(v)); break;
            case byte[] v: WritePayload(s, v.Length); s.Write(v); break;
            case string v: WriteString(s, v); break;
            case int[] v: WritePayload(s, v.Length); foreach (var i in v) WritePayload(s, i); break;
            case long[] v: WritePayload(s, v.Length); foreach (var l in v) WritePayload(s, l); break;
            case List<object> list:
                s.WriteByte(list.Count == 0 ? (byte)0 : TypeOf(list[0]));
                WritePayload(s, list.Count);
                foreach (var item in list) WritePayload(s, item);
                break;
            case Nbt compound:
                foreach (var (name, tag) in compound)
                {
                    s.WriteByte(TypeOf(tag));
                    WriteString(s, name);
                    WritePayload(s, tag);
                }
                s.WriteByte(0); // TAG_End
                break;
            default:
                throw new ArgumentException($"Tipo no soportado: {value.GetType()}");
        }
    }

    private static void WriteString(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
        s.Write(length);
        s.Write(bytes);
    }
}
