using EnderDrive.Core.Cloud;
using EnderDrive.Core.Models;

namespace EnderDrive.Core.Services;

/// <summary>Estado de un mundo respecto a la nube.</summary>
public enum SyncState
{
    /// <summary>Nunca se ha subido.</summary>
    LocalOnly,

    /// <summary>La última copia subida es igual al mundo actual.</summary>
    Synced,

    /// <summary>El mundo ha cambiado desde la última subida.</summary>
    PendingChanges,
}

/// <param name="LastUpload">La copia más reciente de este mundo en la nube (null si no hay).</param>
/// <param name="PendingBytes">Lo que ocupan los archivos modificados desde la última subida.</param>
public record WorldSyncStatus(SyncState State, CloudBackup? LastUpload, long PendingBytes);

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

    /// <summary>
    /// Compara el mundo con su copia más reciente en la nube.
    /// </summary>
    /// <param name="cloudBackups">La lista de la nube (se pide una vez y se reutiliza para todos los mundos).</param>
    Task<WorldSyncStatus> GetStatusAsync(
        string worldFolder,
        IReadOnlyList<CloudBackup> cloudBackups,
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

        // 1. Huella del mundo tal como está ahora (la copia se hace justo después,
        //    y CreateBackupAsync ya comprueba que Minecraft no lo tenga abierto)
        var fingerprint = await Task.Run(() => WorldFingerprint.Compute(worldFolder), cancellationToken);

        // 2. Copia local: también queda en el historial de Copias de Seguridad
        var backup = await backups.CreateBackupAsync(worldFolder, BackupReason.Manual, progress, cancellationToken);

        // 3. Subida, con la huella como etiqueta
        var uploaded = await cloud.UploadBackupAsync(backup, fingerprint, progress, cancellationToken);

        // 4. En la nube guardamos el mismo número de copias por mundo que en local
        await PruneCloudBackupsAsync(uploaded, cancellationToken);

        return uploaded;
    }

    public Task<WorldSyncStatus> GetStatusAsync(
        string worldFolder,
        IReadOnlyList<CloudBackup> cloudBackups,
        CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var worldName = Path.GetFileName(Path.TrimEndingDirectorySeparator(worldFolder));

            // Comparamos con la copia más reciente de ESTE mundo (por nombre de carpeta)
            var lastUpload = cloudBackups
                .Where(b => b.WorldFolderName == worldName)
                .MaxBy(b => b.CreatedAt);

            if (lastUpload is null)
                return new WorldSyncStatus(SyncState.LocalOnly, null, 0);

            if (lastUpload.Fingerprint is not null
                && lastUpload.Fingerprint == WorldFingerprint.Compute(worldFolder))
                return new WorldSyncStatus(SyncState.Synced, lastUpload, 0);

            // Distinta (o copia antigua sin huella): calculamos cuánto ha cambiado
            var pending = WorldFingerprint.ChangedBytesSince(worldFolder, lastUpload.CreatedAt);
            return new WorldSyncStatus(SyncState.PendingChanges, lastUpload, pending);
        }, cancellationToken);

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
