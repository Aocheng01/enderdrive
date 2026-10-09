using EnderDrive.Core.Models;

namespace EnderDrive.Core.Services;

public interface IBackupService
{
    /// <summary>%APPDATA%\EnderDrive\Backups</summary>
    string DefaultBackupsPath { get; }

    /// <summary>La carpeta de copias en uso (la de los ajustes o la de por defecto).</summary>
    string BackupsPath { get; }

    /// <summary>true si Minecraft tiene el mundo abierto (session.lock bloqueado).</summary>
    bool IsWorldInUse(string worldFolder);

    /// <summary>Comprime el mundo en un .zip y borra las copias que sobren.</summary>
    /// <exception cref="WorldInUseException">Si Minecraft tiene el mundo abierto.</exception>
    Task<BackupInfo> CreateBackupAsync(
        string worldFolder,
        BackupReason reason = BackupReason.Manual,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Todas las copias, de la más reciente a la más antigua.</summary>
    Task<IReadOnlyList<BackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sustituye el mundo de <paramref name="savesPath"/> por el contenido de la copia.
    /// Antes guarda una copia automática del estado actual, para poder deshacer.
    /// </summary>
    /// <exception cref="WorldInUseException">Si Minecraft tiene el mundo abierto.</exception>
    Task RestoreAsync(
        BackupInfo backup,
        string savesPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    void Delete(BackupInfo backup);
}

/// <summary>El mundo está abierto en Minecraft y no se puede copiar ni sustituir.</summary>
public sealed class WorldInUseException(string worldFolderName)
    : IOException($"El mundo \"{worldFolderName}\" está abierto en Minecraft. Ciérralo y vuelve a intentarlo.");
