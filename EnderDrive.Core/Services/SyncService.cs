using EnderDrive.Core.Cloud;
using EnderDrive.Core.Models;

namespace EnderDrive.Core.Services;

public interface ISyncService
{
    /// <summary>
    /// Crea una copia del mundo, la sube a la nube y quita de la nube las copias que sobren.
    /// </summary>
    /// <exception cref="WorldInUseException">Si Minecraft tiene el mundo abierto.</exception>
    /// <exception cref="CloudException">Si no hay sesión o falla la subida.</exception>
    Task<CloudBackup> UploadWorldAsync(
        string worldFolder,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Orquesta la sincronización. No sabe nada de Google: trabaja con ICloudProvider,
/// así que sirve igual para cualquier nube (y se puede probar con una nube falsa).
/// </summary>
public sealed class SyncService(
    IBackupService backups,
    ICloudProvider cloud,
    ISettingsService settings) : ISyncService
{
    public async Task<CloudBackup> UploadWorldAsync(
        string worldFolder,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Comprobamos la sesión antes de comprimir: no tiene sentido crear la copia si no se puede subir
        if (!cloud.IsSignedIn)
            throw new CloudException($"No hay ninguna cuenta de {cloud.DisplayName} conectada.");

        // 1. Copia local: también queda en el historial de Copias de Seguridad
        var backup = await backups.CreateBackupAsync(worldFolder, BackupReason.Manual, progress, cancellationToken);

        // 2. Subida
        var uploaded = await cloud.UploadBackupAsync(backup, progress, cancellationToken);

        // 3. En la nube guardamos el mismo número de copias por mundo que en local
        await PruneCloudBackupsAsync(uploaded, cancellationToken);

        return uploaded;
    }

    private async Task PruneCloudBackupsAsync(CloudBackup justUploaded, CancellationToken cancellationToken)
    {
        var max = Math.Max(1, settings.Current.MaxBackupsPerWorld);

        var toDelete = (await cloud.ListBackupsAsync(cancellationToken))
            .Where(b => b.WorldFolderName == justUploaded.WorldFolderName && b.Id != justUploaded.Id)
            .OrderByDescending(b => b.CreatedAt)
            .Skip(max - 1); // -1: la que acabamos de subir ya ocupa un hueco

        foreach (var old in toDelete)
            await cloud.DeleteBackupAsync(old, cancellationToken);
    }
}
