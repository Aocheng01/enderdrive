namespace EnderDrive.Core.Models;

/// <summary>Preferencias del usuario que se guardan entre sesiones.</summary>
public sealed class AppSettings
{
    /// <summary>Carpeta saves elegida por el usuario. null = usar la ruta por defecto.</summary>
    public string? SavesPath { get; set; }

    /// <summary>Carpeta donde se guardan las copias. null = %APPDATA%\EnderDrive\Backups.</summary>
    public string? BackupsPath { get; set; }

    /// <summary>Cuántas copias se conservan por mundo; las más antiguas se borran.</summary>
    public int MaxBackupsPerWorld { get; set; } = 10;
}
