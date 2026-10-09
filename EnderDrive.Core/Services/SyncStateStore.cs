using System.Text.Json;

namespace EnderDrive.Core.Services;

/// <summary>
/// La "base" de un mundo: cómo estaban el mundo y la nube la última vez que se sincronizaron
/// en ESTE PC. Comparando los dos lados con la base sabemos quién ha cambiado.
/// </summary>
/// <param name="LocalFingerprint">Huella del mundo local justo después de sincronizar.</param>
/// <param name="CloudBackupId">La copia de la nube con la que quedó sincronizado.</param>
/// <param name="CloudFingerprint">Huella guardada en esa copia (null en copias antiguas).</param>
public record SyncBase(string LocalFingerprint, string CloudBackupId, string? CloudFingerprint, DateTime SyncedAt);

public interface ISyncStateStore
{
    SyncBase? Get(string worldFolder);

    void Set(string worldFolder, SyncBase syncBase);
}

/// <summary>
/// Guarda las bases en un JSON (%APPDATA%\EnderDrive\sync-state.json).
/// La clave es la ruta completa del mundo: el mismo nombre de mundo en otra carpeta saves
/// es otro mundo distinto.
/// </summary>
public sealed class JsonSyncStateStore : ISyncStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly Dictionary<string, SyncBase> _bases;
    private readonly Lock _lock = new();

    public JsonSyncStateStore(string filePath)
    {
        _filePath = filePath;
        _bases = Load(filePath);
    }

    public SyncBase? Get(string worldFolder)
    {
        lock (_lock)
            return _bases.GetValueOrDefault(Key(worldFolder));
    }

    public void Set(string worldFolder, SyncBase syncBase)
    {
        lock (_lock)
        {
            _bases[Key(worldFolder)] = syncBase;

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_bases, JsonOptions));
            File.Move(tempPath, _filePath, overwrite: true);
        }
    }

    /// <summary>En Windows las rutas no distinguen mayúsculas: normalizamos la clave.</summary>
    private static string Key(string worldFolder)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(worldFolder)).ToLowerInvariant();

    private static Dictionary<string, SyncBase> Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                return JsonSerializer.Deserialize<Dictionary<string, SyncBase>>(File.ReadAllText(filePath)) ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Archivo dañado: empezamos sin bases (los mundos se volverán a comparar)
        }

        return [];
    }
}
