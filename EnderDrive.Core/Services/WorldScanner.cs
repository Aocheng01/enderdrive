using EnderDrive.Core.Models;

namespace EnderDrive.Core.Services;

public sealed class WorldScanner : IWorldScanner
{
    public string DefaultSavesPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".minecraft", "saves");

    public Task<IReadOnlyList<WorldInfo>> ScanAsync(string savesPath, CancellationToken cancellationToken = default)
        => Task.Run(() => Scan(savesPath, cancellationToken), cancellationToken);

    private static IReadOnlyList<WorldInfo> Scan(string savesPath, CancellationToken cancellationToken)
    {
        var worlds = new List<WorldInfo>();

        if (!Directory.Exists(savesPath))
            return worlds;

        foreach (var folder in Directory.EnumerateDirectories(savesPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Una carpeta es un mundo solo si tiene level.dat
            var levelDat = Path.Combine(folder, "level.dat");
            if (!File.Exists(levelDat))
                continue;

            var icon = Path.Combine(folder, "icon.png");
            var folderName = Path.GetFileName(folder);

            // Si level.dat no se puede leer, usamos el nombre de la carpeta y la fecha del archivo
            var level = LevelDatReader.TryRead(levelDat);

            worlds.Add(new WorldInfo(
                Name: level?.LevelName ?? folderName,
                FolderName: folderName,
                FolderPath: folder,
                LastPlayed: level?.LastPlayed ?? File.GetLastWriteTime(levelDat),
                SizeBytes: GetFolderSize(folder),
                IconPath: File.Exists(icon) ? icon : null,
                GameVersion: level?.GameVersion,
                GameMode: level?.GameMode,
                IsHardcore: level?.IsHardcore ?? false));
        }

        return worlds.OrderByDescending(w => w.LastPlayed).ToList();
    }

    private static long GetFolderSize(string folder)
    {
        try
        {
            return new DirectoryInfo(folder)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}