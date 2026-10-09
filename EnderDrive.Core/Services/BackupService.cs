using System.Globalization;
using System.IO.Compression;
using EnderDrive.Core.Models;

namespace EnderDrive.Core.Services;

/// <summary>
/// Copias de seguridad en .zip. Estructura en disco:
/// <c>{BackupsPath}\{carpeta del mundo}\2026-10-09_18-30-05.zip</c>
/// (con el sufijo <c>_antes-de-restaurar</c> si es automática).
/// </summary>
public sealed class BackupService(ISettingsService settings) : IBackupService
{
    private const string DateFormat = "yyyy-MM-dd_HH-mm-ss";
    private const string BeforeRestoreSuffix = "_antes-de-restaurar";
    private const string SessionLock = "session.lock";

    // Solo una operación a la vez: dos copias o restauraciones a la vez podrían pisarse
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string DefaultBackupsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EnderDrive", "Backups");

    public string BackupsPath => settings.Current.BackupsPath ?? DefaultBackupsPath;

    public bool IsWorldInUse(string worldFolder)
    {
        // Mientras un mundo está abierto, Minecraft mantiene session.lock abierto.
        // Si no podemos abrirlo en exclusiva (FileShare.None), es que alguien lo está usando.
        var lockFile = Path.Combine(worldFolder, SessionLock);
        if (!File.Exists(lockFile))
            return false;

        try
        {
            using var _ = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public async Task<BackupInfo> CreateBackupAsync(
        string worldFolder,
        BackupReason reason = BackupReason.Manual,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await CreateBackupCoreAsync(worldFolder, reason, "Comprimiendo", progress, protectedFile: null, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<BackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<BackupInfo>>(() =>
        {
            if (!Directory.Exists(BackupsPath))
                return [];

            return NewestFirst(Directory.EnumerateDirectories(BackupsPath).SelectMany(ListWorldBackups))
                .ToList();
        }, cancellationToken);

    public async Task RestoreAsync(
        BackupInfo backup,
        string savesPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var worldFolder = Path.Combine(savesPath, backup.WorldFolderName);

            // 1. Guardamos el estado actual, por si el usuario quiere deshacer la restauración.
            //    La copia que vamos a restaurar queda protegida de la limpieza de copias antiguas.
            if (Directory.Exists(worldFolder))
            {
                if (IsWorldInUse(worldFolder))
                    throw new WorldInUseException(backup.WorldFolderName);

                await CreateBackupCoreAsync(worldFolder, BackupReason.BeforeRestore, "Guardando el estado actual",
                    progress, protectedFile: backup.FilePath, cancellationToken);
            }

            // 2. Extraemos en una carpeta temporal. Si algo falla aquí, el mundo actual no se toca.
            Directory.CreateDirectory(savesPath);
            var tempFolder = Path.Combine(savesPath, $".{backup.WorldFolderName}.enderdrive-restore");
            var oldFolder = Path.Combine(savesPath, $".{backup.WorldFolderName}.enderdrive-old");
            TryDeleteDirectory(tempFolder);
            TryDeleteDirectory(oldFolder);

            try
            {
                await Task.Run(() => ExtractZip(backup.FilePath, tempFolder, progress, cancellationToken), cancellationToken);

                // 3. Intercambiamos las carpetas. Mover una carpeta es casi instantáneo.
                if (Directory.Exists(worldFolder))
                    Directory.Move(worldFolder, oldFolder);

                try
                {
                    Directory.Move(tempFolder, worldFolder);
                }
                catch
                {
                    // Si no se pudo colocar la restaurada, devolvemos el mundo original a su sitio
                    if (Directory.Exists(oldFolder))
                        Directory.Move(oldFolder, worldFolder);
                    throw;
                }

                TryDeleteDirectory(oldFolder);
            }
            finally
            {
                TryDeleteDirectory(tempFolder);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Delete(BackupInfo backup)
    {
        File.Delete(backup.FilePath);

        // Si era la última copia del mundo, quitamos también su carpeta vacía
        var folder = Path.GetDirectoryName(backup.FilePath);
        if (folder is not null && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            Directory.Delete(folder);
    }

    private async Task<BackupInfo> CreateBackupCoreAsync(
        string worldFolder,
        BackupReason reason,
        string stage,
        IProgress<OperationProgress>? progress,
        string? protectedFile,
        CancellationToken cancellationToken)
    {
        var worldName = Path.GetFileName(Path.TrimEndingDirectorySeparator(worldFolder));

        if (!Directory.Exists(worldFolder))
            throw new DirectoryNotFoundException($"No existe la carpeta del mundo: {worldFolder}");

        if (IsWorldInUse(worldFolder))
            throw new WorldInUseException(worldName);

        var targetFolder = Path.Combine(BackupsPath, worldName);
        Directory.CreateDirectory(targetFolder);

        var createdAt = DateTime.Now;
        var filePath = GetUniqueBackupPath(targetFolder, createdAt, reason);

        // Escribimos en .tmp y renombramos al final: nunca queda un .zip a medias con nombre válido
        var tempPath = filePath + ".tmp";
        try
        {
            await Task.Run(() => WriteZip(worldFolder, tempPath, stage, progress, cancellationToken), cancellationToken);
            File.Move(tempPath, filePath);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        var backup = new BackupInfo(worldName, filePath, createdAt, new FileInfo(filePath).Length, reason);
        PruneOldBackups(targetFolder, keep: [backup.FilePath, protectedFile]);
        return backup;
    }

    private static void WriteZip(
        string worldFolder, string zipPath, string stage,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var files = new DirectoryInfo(worldFolder)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.Name.Equals(SessionLock, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var reporter = new ProgressReporter(stage, files.Sum(f => f.Length), progress);
        var buffer = new byte[81920];

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Las rutas dentro de un zip usan siempre "/"
            var entryName = Path.GetRelativePath(worldFolder, file.FullName).Replace('\\', '/');

            // Los .mca (regiones) ya guardan los chunks comprimidos: recomprimirlos es lento y apenas ahorra
            var level = file.Extension.Equals(".mca", StringComparison.OrdinalIgnoreCase)
                ? CompressionLevel.NoCompression
                : CompressionLevel.Fastest;

            var entry = zip.CreateEntry(entryName, level);
            if (file.LastWriteTime.Year >= 1980) // el formato zip no admite fechas anteriores
                entry.LastWriteTime = file.LastWriteTime;

            // FileShare.ReadWrite: podemos leer aunque otro programa tenga el archivo abierto
            using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var target = entry.Open();

            int read;
            while ((read = source.Read(buffer)) > 0)
            {
                target.Write(buffer, 0, read);
                reporter.Add(read);
            }
        }
    }

    private static void ExtractZip(
        string zipPath, string destination,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        if (zip.GetEntry("level.dat") is null)
            throw new InvalidDataException("La copia no contiene level.dat: no parece un mundo de Minecraft.");

        var reporter = new ProgressReporter("Restaurando", zip.Entries.Sum(e => e.Length), progress);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var buffer = new byte[81920];

        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Protección "zip slip": una entrada como "../../algo" no puede escribir fuera del destino
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Entrada no válida en la copia: {entry.FullName}");

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var source = entry.Open())
            using (var output = File.Create(target))
            {
                int read;
                while ((read = source.Read(buffer)) > 0)
                {
                    output.Write(buffer, 0, read);
                    reporter.Add(read);
                }
            }
            File.SetLastWriteTime(target, entry.LastWriteTime.LocalDateTime);
        }
    }

    private void PruneOldBackups(string worldBackupsFolder, string?[] keep)
    {
        var max = Math.Max(1, settings.Current.MaxBackupsPerWorld);

        var toDelete = NewestFirst(ListWorldBackups(worldBackupsFolder))
            .Skip(max)
            .Where(b => !keep.Contains(b.FilePath, StringComparer.OrdinalIgnoreCase));

        foreach (var old in toDelete)
            TryDeleteFile(old.FilePath);
    }

    /// <summary>
    /// De la más reciente a la más antigua. El nombre solo guarda hasta los segundos, así que si
    /// hay dos copias del mismo segundo desempatamos con la hora exacta de escritura del archivo.
    /// </summary>
    private static IOrderedEnumerable<BackupInfo> NewestFirst(IEnumerable<BackupInfo> backups)
        => backups
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => File.GetLastWriteTimeUtc(b.FilePath));

    private static IEnumerable<BackupInfo> ListWorldBackups(string worldBackupsFolder)
    {
        var worldName = Path.GetFileName(worldBackupsFolder);

        foreach (var file in new DirectoryInfo(worldBackupsFolder).EnumerateFiles("*.zip"))
        {
            var name = Path.GetFileNameWithoutExtension(file.Name);
            var reason = name.Contains(BeforeRestoreSuffix, StringComparison.OrdinalIgnoreCase)
                ? BackupReason.BeforeRestore
                : BackupReason.Manual;

            // La fecha va en el nombre; si alguien lo renombró, usamos la del archivo
            var createdAt = name.Length >= DateFormat.Length
                && DateTime.TryParseExact(name[..DateFormat.Length], DateFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : file.LastWriteTime;

            yield return new BackupInfo(worldName, file.FullName, createdAt, file.Length, reason);
        }
    }

    private static string GetUniqueBackupPath(string folder, DateTime createdAt, BackupReason reason)
    {
        var baseName = createdAt.ToString(DateFormat, CultureInfo.InvariantCulture)
            + (reason == BackupReason.BeforeRestore ? BeforeRestoreSuffix : "");

        var path = Path.Combine(folder, baseName + ".zip");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{baseName}_{i}.zip");

        return path;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No es crítico: como mucho queda un archivo temporal huérfano
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No es crítico: como mucho queda una carpeta temporal huérfana
        }
    }

    /// <summary>Informa del progreso solo cuando cambia el porcentaje, para no saturar la interfaz.</summary>
    private sealed class ProgressReporter
    {
        private readonly string _stage;
        private readonly long _total;
        private readonly IProgress<OperationProgress>? _progress;
        private long _done;
        private int _lastPercent = -1;

        public ProgressReporter(string stage, long total, IProgress<OperationProgress>? progress)
        {
            _stage = stage;
            _total = total;
            _progress = progress;
            Add(0);
        }

        public void Add(long bytes)
        {
            _done += bytes;
            var percent = _total <= 0 ? 100 : (int)(_done * 100 / _total);
            if (percent == _lastPercent)
                return;

            _lastPercent = percent;
            _progress?.Report(new OperationProgress(_stage, _done, _total));
        }
    }
}
