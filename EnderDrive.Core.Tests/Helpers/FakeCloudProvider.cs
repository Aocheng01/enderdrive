using EnderDrive.Core.Cloud;
using EnderDrive.Core.Models;

namespace EnderDrive.Core.Tests.Helpers;

/// <summary>
/// Nube falsa: guarda las "subidas" en una carpeta local. Así probamos SyncService
/// sin Internet ni cuenta de Google, y sin tocar nada real.
/// </summary>
public sealed class FakeCloudProvider(string storageFolder) : ICloudProvider
{
    private readonly Dictionary<string, string> _contents = new(); // id → archivo con el contenido
    private int _nextId;

    public List<CloudBackup> Files { get; } = [];
    public List<CloudBackup> Trash { get; } = [];
    public List<string> UploadedPaths { get; } = [];

    public string Id => "fake";
    public string DisplayName => "Nube de prueba";
    public bool IsConfigured => true;
    public bool IsSignedIn { get; set; } = true;

    public Task<CloudBackup> UploadBackupAsync(
        BackupInfo backup, string? fingerprint, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new OperationProgress("Subiendo", backup.SizeBytes, backup.SizeBytes));
        UploadedPaths.Add(backup.FilePath);
        return Task.FromResult(Store(backup.WorldFolderName, backup.FilePath, backup.CreatedAt, fingerprint));
    }

    /// <summary>Simula que otro PC ha subido una copia de este mundo.</summary>
    public CloudBackup AddRemoteBackup(string worldFolderName, string zipPath, string? fingerprint, DateTime? createdAt = null)
        => Store(worldFolderName, zipPath, createdAt ?? DateTime.Now.AddSeconds(5), fingerprint);

    public Task<IReadOnlyList<CloudBackup>> ListBackupsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CloudBackup>>(Files.OrderByDescending(f => f.CreatedAt).ToList());

    public Task DeleteBackupAsync(CloudBackup backup, CancellationToken cancellationToken = default)
    {
        Files.Remove(backup);
        Trash.Add(backup);
        return Task.CompletedTask;
    }

    public Task DownloadBackupAsync(
        CloudBackup backup, string destinationFile, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new OperationProgress("Descargando", backup.SizeBytes, backup.SizeBytes));
        File.Copy(_contents[backup.Id], destinationFile, overwrite: true);
        return Task.CompletedTask;
    }

    private CloudBackup Store(string world, string zipPath, DateTime createdAt, string? fingerprint)
    {
        Directory.CreateDirectory(storageFolder);
        var id = $"id-{_nextId++}";
        var stored = Path.Combine(storageFolder, id + ".zip");
        File.Copy(zipPath, stored);
        _contents[id] = stored;

        var backup = new CloudBackup(id, world, Path.GetFileName(zipPath), createdAt,
            new FileInfo(stored).Length, BackupReason.Manual, fingerprint);
        Files.Add(backup);
        return backup;
    }

    // La parte de la cuenta no se usa en estos tests
    public Task<CloudAccount?> RestoreSessionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CloudAccount> SignInAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CloudAccount> GetAccountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SignOutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
