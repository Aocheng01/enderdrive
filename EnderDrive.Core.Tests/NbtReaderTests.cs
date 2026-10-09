using EnderDrive.Core.Nbt;
using EnderDrive.Core.Tests.Helpers;
using NbtData = EnderDrive.Core.Tests.Helpers.Nbt;

namespace EnderDrive.Core.Tests;

public class NbtReaderTests
{
    [Fact]
    public void Lee_todos_los_tipos_de_etiqueta()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("todo.dat");
        NbtData.WriteFile(path, new NbtData
        {
            ["byte"] = (sbyte)-5,
            ["short"] = (short)1234,
            ["int"] = 123456,
            ["long"] = -9185040100292446419L,
            ["float"] = 1.5f,
            ["double"] = 2.25,
            ["string"] = "Valle Esmeralda ñ",
            ["bytes"] = new byte[] { 1, 2, 3 },
            ["ints"] = new[] { 7, 8 },
            ["longs"] = new[] { 9L },
            ["list"] = new List<object> { "a", "b" },
            ["compound"] = new NbtData { ["inner"] = 42 },
        });

        var root = NbtReader.ReadFile(path);

        Assert.Equal((sbyte)-5, root.Tags["byte"]);
        Assert.Equal((short)1234, root.Tags["short"]);
        Assert.Equal(123456, root.GetInt("int"));
        Assert.Equal(-9185040100292446419L, root.GetLong("long"));
        Assert.Equal(1.5f, root.Tags["float"]);
        Assert.Equal(2.25, root.Tags["double"]);
        Assert.Equal("Valle Esmeralda ñ", root.GetString("string"));
        Assert.Equal(new byte[] { 1, 2, 3 }, root.Tags["bytes"]);
        Assert.Equal(new[] { 7, 8 }, root.Tags["ints"]);
        Assert.Equal(new[] { 9L }, root.Tags["longs"]);
        Assert.Equal(new object[] { "a", "b" }, root.GetList("list"));
        Assert.Equal(42, root.GetCompound("compound")?.GetInt("inner"));
    }

    [Fact]
    public void Lee_archivos_sin_comprimir()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("plano.dat");
        NbtData.WriteFile(path, new NbtData { ["valor"] = 7 }, gzip: false);

        Assert.Equal(7, NbtReader.ReadFile(path).GetInt("valor"));
    }

    [Fact]
    public void Los_getters_devuelven_null_si_falta_la_etiqueta_o_es_de_otro_tipo()
    {
        var root = NbtReader.Read(ToStream(new NbtData { ["texto"] = "hola", ["flag"] = (sbyte)1 }));

        Assert.Null(root.GetString("no-existe"));
        Assert.Null(root.GetInt("texto"));      // existe, pero no es un int
        Assert.Null(root.GetCompound("texto"));
        Assert.True(root.GetBool("flag"));
    }

    [Fact]
    public void Falla_si_la_raiz_no_es_un_compound()
    {
        var stream = new MemoryStream([8, 0, 0]); // TAG_String en vez de TAG_Compound

        Assert.Throws<InvalidDataException>(() => NbtReader.Read(stream));
    }

    [Fact]
    public void Falla_si_el_archivo_esta_cortado()
    {
        var bytes = ToStream(new NbtData { ["nombre"] = "Mundo" }).ToArray();
        var cortado = new MemoryStream(bytes[..^4]);

        Assert.Throws<EndOfStreamException>(() => NbtReader.Read(cortado));
    }

    [Fact]
    public void Falla_con_un_tipo_de_etiqueta_desconocido()
    {
        // Compound raíz con una etiqueta de tipo 99
        var stream = new MemoryStream([10, 0, 0, 99, 0, 1, (byte)'x', 0]);

        Assert.Throws<InvalidDataException>(() => NbtReader.Read(stream));
    }

    private static MemoryStream ToStream(NbtData root)
    {
        var stream = new MemoryStream();
        NbtData.Write(stream, root);
        stream.Position = 0;
        return stream;
    }
}
