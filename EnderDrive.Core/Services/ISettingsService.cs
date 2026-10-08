using EnderDrive.Core.Models;

namespace EnderDrive.Core.Services;

public interface ISettingsService
{
    /// <summary>Ajustes actuales. Se cargan al crear el servicio.</summary>
    AppSettings Current { get; }

    /// <summary>Guarda <see cref="Current"/> en disco.</summary>
    void Save();
}
