using EnderDrive.Core.Cloud;
using EnderDrive.Core.Models;

namespace EnderDrive.Core.Services;

/// <summary>Estado de un mundo respecto a la nube.</summary>
public enum SyncState
{
    /// <summary>Nunca se ha subido.</summary>
    LocalOnly,

    /// <summary>El mundo local y la nube están igual que en la última sincronización.</summary>
    Synced,

    /// <summary>Solo ha cambiado el mundo local: hay que subir.</summary>
    PendingChanges,

    /// <summary>Solo ha cambiado la nube (se subió desde otro PC): hay que descargar.</summary>
    CloudNewer,

    /// <summary>Han cambiado los dos lados desde la última sincronización.</summary>
    Conflict,

    /// <summary>Está en la nube pero no en este PC.</summary>
    CloudOnly,
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
    /// Descarga una copia de la nube y la instala en <paramref name="savesPath"/>. Si el mundo
    /// ya existe, antes se guarda una copia local de su estado actual (como al restaurar).
    /// </summary>
    /// <exception cref="WorldInUseException">Si Minecraft tiene el mundo abierto.</exception>
    Task DownloadWorldAsync(
        CloudBackup backup,
        string savesPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Compara el mundo y la nube con su última sincronización en este PC.</summary>
    /// <param name="cloudBackups">La lista de la nube (se pide una vez y se reutiliza para todos los mundos).</param>
    Task<WorldSyncStatus> GetStatusAsync(
        string worldFolder,
        IReadOnlyList<CloudBackup> cloudBackups,
        CancellationToken cancellationToken = default);

    /// <summary>La copia más reciente de cada mundo que está en la nube pero no en este PC.</summary>
    IReadOnlyList<CloudBackup> GetCloudOnlyWorlds(IReadOnlyList<CloudBackup> cloudBackups, IEnumerable<string> localWorldFolderNames);
}

/// <summary>
/// Orquesta la sincronización. No sabe nada de Google: trabaja con ICloudProvider,
/// así que sirve igual para cualquier nube (y se puede probar con una nube falsa).
/// </summary>
public sealed class SyncService(
    IBackupService backups,
    ICloudProvider cloud,
    ISettingsService settings,
    ISyncStateStore syncState) : ISyncService
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

        // 4. Nueva base: ahora el mundo y la nube están de acuerdo
        syncState.Set(worldFolder, new SyncBase(fingerprint, uploaded.Id, fingerprint, DateTime.Now));

        // 5. En la nube guardamos el mismo número de copias por mundo que en local
        await PruneCloudBackupsAsync(uploaded, cancellationToken);

        return uploaded;
    }

    public async Task DownloadWorldAsync(
        CloudBackup backup,
        string savesPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!cloud.IsSignedIn)
            throw new CloudException($"No hay ninguna cuenta de {cloud.DisplayName} conectada.");

        var worldFolder = Path.Combine(savesPath, backup.WorldFolderName);
        if (Directory.Exists(worldFolder) && backups.IsWorldInUse(worldFolder))
            throw new WorldInUseException(backup.WorldFolderName); // antes de descargar nada

        // 1. Bajamos el .zip a una carpeta temporal
        var tempFolder = Path.Combine(Path.GetTempPath(), "EnderDrive", "downloads");
        Directory.CreateDirectory(tempFolder);
        var tempZip = Path.Combine(tempFolder, $"{Guid.NewGuid():N}.zip");

        try
        {
            await cloud.DownloadBackupAsync(backup, tempZip, progress, cancellationToken);

            // 2. Lo instalamos con la restauración segura de siempre: copia previa del estado
            //    actual, comprobación de level.dat, protección "zip slip" e intercambio de carpetas
            var local = new BackupInfo(backup.WorldFolderName, tempZip, backup.CreatedAt,
                new FileInfo(tempZip).Length, backup.Reason);
            await backups.RestoreAsync(local, savesPath, progress, cancellationToken);
        }
        finally
        {
            try
            {
                File.Delete(tempZip);
            }
            catch (IOException)
            {
                // Como mucho queda un temporal huérfano
            }
        }

        // 3. Nueva base. Si se bajó una versión antigua, la base apunta a la MÁS RECIENTE de la nube:
        //    así el mundo queda como "cambios pendientes" (súbelo para que esa versión sea la actual)
        //    en lugar de "hay una versión más nueva en la nube", que invitaría a deshacer lo que acabas de hacer.
        var latest = (await cloud.ListBackupsAsync(cancellationToken))
            .Where(b => b.WorldFolderName == backup.WorldFolderName)
            .MaxBy(b => b.CreatedAt) ?? backup;

        // Si es la más reciente, la base es el mundo tal como ha quedado instalado.
        // Si es antigua, la base es la versión más reciente: el mundo local "difiere" de ella
        // y por eso aparece como cambios pendientes de subir.
        var localBase = latest.Id == backup.Id
            ? await Task.Run(() => WorldFingerprint.Compute(worldFolder), cancellationToken)
            : latest.Fingerprint ?? "";
        syncState.Set(worldFolder, new SyncBase(localBase, latest.Id, latest.Fingerprint, DateTime.Now));
    }

    public Task<WorldSyncStatus> GetStatusAsync(
        string worldFolder,
        IReadOnlyList<CloudBackup> cloudBackups,
        CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var worldName = Path.GetFileName(Path.TrimEndingDirectorySeparator(worldFolder));

            var lastUpload = cloudBackups
                .Where(b => b.WorldFolderName == worldName)
                .MaxBy(b => b.CreatedAt);

            if (lastUpload is null)
                return new WorldSyncStatus(SyncState.LocalOnly, null, 0);

            var local = WorldFingerprint.Compute(worldFolder);
            var syncBase = syncState.Get(worldFolder);

            SyncState state;
            if (syncBase is null)
            {
                // Nunca se ha sincronizado en este PC (o se subió antes de que existieran las bases)
                if (lastUpload.Fingerprint is null)
                    state = SyncState.PendingChanges; // copia antigua sin huella: hay que volver a subir
                else if (lastUpload.Fingerprint == local)
                {
                    // Coinciden: adoptamos esta situación como base para la próxima vez
                    syncState.Set(worldFolder, new SyncBase(local, lastUpload.Id, lastUpload.Fingerprint, DateTime.Now));
                    state = SyncState.Synced;
                }
                else
                    state = SyncState.Conflict; // distintos y sin base: no sabemos cuál es el bueno
            }
            else
            {
                // Comparamos cada lado con la base (como hace Git con el último punto en común)
                var localChanged = local != syncBase.LocalFingerprint;
                var cloudChanged = lastUpload.Id != syncBase.CloudBackupId
                    && (lastUpload.Fingerprint is null || lastUpload.Fingerprint != syncBase.CloudFingerprint);

                state = (localChanged, cloudChanged) switch
                {
                    (false, false) => SyncState.Synced,
                    (true, false) => SyncState.PendingChanges,
                    (false, true) => SyncState.CloudNewer,
                    (true, true) => SyncState.Conflict,
                };
            }

            var pending = state is SyncState.PendingChanges or SyncState.Conflict
                ? WorldFingerprint.ChangedBytesSince(worldFolder, syncBase?.SyncedAt ?? lastUpload.CreatedAt)
                : 0;

            return new WorldSyncStatus(state, lastUpload, pending);
        }, cancellationToken);

    public IReadOnlyList<CloudBackup> GetCloudOnlyWorlds(
        IReadOnlyList<CloudBackup> cloudBackups, IEnumerable<string> localWorldFolderNames)
    {
        var local = localWorldFolderNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return cloudBackups
            .Where(b => !local.Contains(b.WorldFolderName))
            .GroupBy(b => b.WorldFolderName)
            .Select(g => g.MaxBy(b => b.CreatedAt)!)
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
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
