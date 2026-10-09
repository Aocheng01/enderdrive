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

    /// <summary>Al salir de un mundo en Minecraft, subir sus cambios (solo mundos que ya están en la nube).</summary>
    public bool AutoUploadOnWorldClose { get; set; } = true;

    /// <summary>Al abrir EnderDrive, avisar si hay versiones nuevas en la nube o conflictos.</summary>
    public bool NotifyCloudChangesOnStartup { get; set; } = true;
}
