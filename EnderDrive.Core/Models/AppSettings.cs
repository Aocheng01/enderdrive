namespace EnderDrive.Core.Models;

/// <summary>Preferencias del usuario que se guardan entre sesiones.</summary>
public sealed class AppSettings
{
    /// <summary>Carpeta saves elegida por el usuario. null = usar la ruta por defecto.</summary>
    public string? SavesPath { get; set; }
}
