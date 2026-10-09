using EnderDrive.Core.Cloud;
using EnderDrive.Core.Models;

namespace EnderDrive.Core.Tests.Helpers;

/// <summary>
/// Nube falsa en memoria: guarda las "subidas" en una lista. Así probamos SyncService
/// sin Internet ni cuenta de Google, y sin tocar nada real.
/// </summary>
public sealed class FakeCloudProvider : ICloudProvider
{
    private int _nextId;

    public List<CloudBackup> Files { get; } = [];
    public List<CloudBackup> Trash { get; } = [];
    public List<string> UploadedPaths { get; } = [];

    public string Id => "fake";
    public string DisplayName => "Nube de prueba";
    public bool IsConfigured => true;
    public bool IsSignedIn { get; set; } = true;

    public Task<CloudBackup> UploadBackupAsync(
        BackupInfo backup, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new OperationProgress("Subiendo", backup.SizeBytes, backup.SizeBytes));

        var uploaded = new CloudBackup($"id-{_nextId++}", backup.WorldFolderName, Path.GetFileName(backup.FilePath),
            backup.CreatedAt, backup.SizeBytes, backup.Reason);
        Files.Add(uploaded);
        UploadedPaths.Add(backup.FilePath);
        return Task.FromResult(uploaded);
    }

    public Task<IReadOnlyList<CloudBackup>> ListBackupsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CloudBackup>>(Files.OrderByDescending(f => f.CreatedAt).ToList());

    public Task DeleteBackupAsync(CloudBackup backup, CancellationToken cancellationToken = default)
    {
        Files.Remove(backup);
        Trash.Add(backup);
        return Task.CompletedTask;
    }

    // La parte de la cuenta no se usa en estos tests
    public Task<CloudAccount?> RestoreSessionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CloudAccount> SignInAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CloudAccount> GetAccountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SignOutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
