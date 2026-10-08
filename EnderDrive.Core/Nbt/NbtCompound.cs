namespace EnderDrive.Core.Nbt;

/// <summary>
/// Etiqueta compuesta de NBT (TAG_Compound): un diccionario nombre → valor.
/// Los valores son: sbyte, short, int, long, float, double, string, byte[], int[], long[],
/// List&lt;object&gt; o NbtCompound.
/// </summary>
public sealed class NbtCompound
{
    private readonly Dictionary<string, object> _tags = new();

    public IReadOnlyDictionary<string, object> Tags => _tags;

    internal void Add(string name, object value) => _tags[name] = value;

    public NbtCompound? GetCompound(string name) => _tags.GetValueOrDefault(name) as NbtCompound;

    public string? GetString(string name) => _tags.GetValueOrDefault(name) as string;

    public int? GetInt(string name) => _tags.GetValueOrDefault(name) is int value ? value : null;

    public long? GetLong(string name) => _tags.GetValueOrDefault(name) is long value ? value : null;

    public IReadOnlyList<object>? GetList(string name) => _tags.GetValueOrDefault(name) as List<object>;

    /// <summary>NBT no tiene booleanos: se guardan como TAG_Byte (0 o 1).</summary>
    public bool? GetBool(string name) => _tags.GetValueOrDefault(name) is sbyte value ? value != 0 : null;
}
