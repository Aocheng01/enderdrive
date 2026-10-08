using System.Text.Json;
using EnderDrive.Core.Models;

namespace EnderDrive.Core.Services;

/// <summary>Guarda los ajustes en %APPDATA%\EnderDrive\settings.json.</summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EnderDrive", "settings.json");

    public AppSettings Current { get; }

    public JsonSettingsService()
    {
        Current = Load();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        // Escribimos en un archivo temporal y luego lo movemos:
        // si la app se cierra a mitad, el settings.json anterior no se corrompe
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Archivo dañado o inaccesible: empezamos con ajustes por defecto
        }

        return new AppSettings();
    }
}
