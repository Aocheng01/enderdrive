using System.IO.Compression;
using EnderDrive.Core.Models;
using EnderDrive.Core.Services;
using EnderDrive.Core.Tests.Helpers;

namespace EnderDrive.Core.Tests;

/// <summary>
/// Cada test tiene su propia carpeta temporal con un "saves" y una carpeta de copias.
/// xUnit crea una instancia nueva de la clase por cada test, así que no se pisan entre sí.
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeSettings _settings = new();
    private readonly BackupService _service;
    private readonly string _saves;
    private readonly string _world;

    public BackupServiceTests()
    {
        _saves = _temp.Combine("saves");
        _settings.Current.BackupsPath = _temp.Combine("backups");
        _service = new BackupService(_settings);
        _world = TestWorlds.Create121(_saves, "Mi Mundo");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Crea_un_zip_con_el_mundo_sin_session_lock()
    {
        var backup = await _service.CreateBackupAsync(_world);

        Assert.True(File.Exists(backup.FilePath));
        Assert.Equal("Mi Mundo", backup.WorldFolderName);
        Assert.Equal(BackupReason.Manual, backup.Reason);

        using var zip = ZipFile.OpenRead(backup.FilePath);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("level.dat", entries);
        Assert.Contains("region/r.0.0.mca", entries); // con "/" aunque estemos en Windows
        Assert.DoesNotContain("session.lock", entries);
    }

    [Fact]
    public async Task Informa_del_progreso_hasta_el_100_por_cien()
    {
        var reports = new List<OperationProgress>();

        await _service.CreateBackupAsync(_world, progress: new SyncProgress(reports.Add));

        Assert.NotEmpty(reports);
        Assert.Equal(100, reports[^1].Percent);
    }

    [Fact]
    public async Task Borra_las_copias_que_pasan_del_maximo()
    {
        _settings.Current.MaxBackupsPerWorld = 2;

        var first = await _service.CreateBackupAsync(_world);
        await _service.CreateBackupAsync(_world);
        await _service.CreateBackupAsync(_world);

        var backups = await _service.GetBackupsAsync();
        Assert.Equal(2, backups.Count);
        Assert.False(File.Exists(first.FilePath));
    }

    [Fact]
    public async Task Restaurar_devuelve_el_mundo_a_como_estaba()
    {
        var backup = await _service.CreateBackupAsync(_world);

        // Cambiamos el mundo después de la copia
        File.WriteAllText(Path.Combine(_world, "nuevo.txt"), "no debería sobrevivir");
        File.Delete(Path.Combine(_world, "icon.png"));

        await _service.RestoreAsync(backup, _saves);

        Assert.False(File.Exists(Path.Combine(_world, "nuevo.txt")));
        Assert.True(File.Exists(Path.Combine(_world, "icon.png")));
        Assert.True(File.Exists(Path.Combine(_world, "region", "r.0.0.mca")));
    }

    [Fact]
    public async Task Restaurar_guarda_antes_una_copia_del_estado_actual()
    {
        var backup = await _service.CreateBackupAsync(_world);
        File.WriteAllText(Path.Combine(_world, "cambio.txt"), "estado actual");

        await _service.RestoreAsync(backup, _saves);

        var automatic = Assert.Single(await _service.GetBackupsAsync(), b => b.Reason == BackupReason.BeforeRestore);
        using var zip = ZipFile.OpenRead(automatic.FilePath);
        Assert.NotNull(zip.GetEntry("cambio.txt")); // se puede deshacer la restauración
    }

    [Fact]
    public async Task La_copia_que_se_restaura_no_se_borra_aunque_pase_del_maximo()
    {
        _settings.Current.MaxBackupsPerWorld = 1;
        var backup = await _service.CreateBackupAsync(_world);

        // La copia automática previa a restaurar haría que "backup" sobrara
        await _service.RestoreAsync(backup, _saves);

        Assert.True(File.Exists(backup.FilePath));
    }

    [Fact]
    public async Task Restaura_aunque_el_mundo_se_haya_borrado()
    {
        var backup = await _service.CreateBackupAsync(_world);
        Directory.Delete(_world, recursive: true);

        await _service.RestoreAsync(backup, _saves);

        Assert.True(File.Exists(Path.Combine(_world, "level.dat")));
        Assert.DoesNotContain(await _service.GetBackupsAsync(), b => b.Reason == BackupReason.BeforeRestore);
    }

    [Fact]
    public async Task No_deja_carpetas_temporales_en_saves()
    {
        var backup = await _service.CreateBackupAsync(_world);

        await _service.RestoreAsync(backup, _saves);

        Assert.Equal(["Mi Mundo"], Directory.GetDirectories(_saves).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Detecta_un_mundo_abierto_en_Minecraft()
    {
        Assert.False(_service.IsWorldInUse(_world));

        // Minecraft mantiene session.lock abierto mientras juegas
        using (OpenSessionLock())
        {
            Assert.True(_service.IsWorldInUse(_world));
            await Assert.ThrowsAsync<WorldInUseException>(() => _service.CreateBackupAsync(_world));
        }

        Assert.False(_service.IsWorldInUse(_world));
    }

    [Fact]
    public async Task No_restaura_encima_de_un_mundo_abierto()
    {
        var backup = await _service.CreateBackupAsync(_world);
        File.WriteAllText(Path.Combine(_world, "partida.txt"), "en curso");

        using (OpenSessionLock())
            await Assert.ThrowsAsync<WorldInUseException>(() => _service.RestoreAsync(backup, _saves));

        Assert.True(File.Exists(Path.Combine(_world, "partida.txt"))); // no se tocó nada
    }

    [Fact]
    public async Task Rechaza_un_zip_que_no_es_un_mundo()
    {
        var fake = await CreateZipBackupAsync(("leeme.txt", "hola"));
        File.WriteAllText(Path.Combine(_world, "marca.txt"), "x");

        await Assert.ThrowsAsync<InvalidDataException>(() => _service.RestoreAsync(fake, _saves));

        Assert.True(File.Exists(Path.Combine(_world, "marca.txt"))); // el mundo actual sigue intacto
    }

    [Fact]
    public async Task Rechaza_entradas_que_escriben_fuera_del_mundo()
    {
        // "Zip slip": una entrada con ../ intentaría escribir fuera de la carpeta destino
        var malicious = await CreateZipBackupAsync(("level.dat", "x"), ("../../fuera.txt", "malo"));

        await Assert.ThrowsAsync<InvalidDataException>(() => _service.RestoreAsync(malicious, _saves));

        Assert.False(File.Exists(_temp.Combine("fuera.txt")));
        Assert.True(File.Exists(Path.Combine(_world, "region", "r.0.0.mca")));
    }

    [Fact]
    public async Task Eliminar_borra_el_archivo_y_la_carpeta_vacia()
    {
        var backup = await _service.CreateBackupAsync(_world);

        _service.Delete(backup);

        Assert.False(File.Exists(backup.FilePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(backup.FilePath)));
        Assert.Empty(await _service.GetBackupsAsync());
    }

    [Fact]
    public async Task Lista_las_copias_de_todos_los_mundos()
    {
        var other = TestWorlds.Create26(_saves, "Otro Mundo", seed: 1, hardcore: false);

        await _service.CreateBackupAsync(_world);
        await _service.CreateBackupAsync(other);

        var worlds = (await _service.GetBackupsAsync()).Select(b => b.WorldFolderName).Order();
        Assert.Equal(["Mi Mundo", "Otro Mundo"], worlds);
    }

    private FileStream OpenSessionLock()
        => new(Path.Combine(_world, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

    /// <summary>Crea a mano un .zip en la carpeta de copias, con las entradas que queramos.</summary>
    private async Task<BackupInfo> CreateZipBackupAsync(params (string Name, string Content)[] entries)
    {
        var folder = Path.Combine(_settings.Current.BackupsPath!, "Mi Mundo");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "2020-01-01_00-00-00.zip");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in entries)
            {
                await using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                await writer.WriteAsync(content);
            }
        }

        return new BackupInfo("Mi Mundo", path, new DateTime(2020, 1, 1), new FileInfo(path).Length, BackupReason.Manual);
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public void Save()
        {
        }
    }

    /// <summary>A diferencia de Progress&lt;T&gt;, informa al momento (sin pasar por otro hilo).</summary>
    private sealed class SyncProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
