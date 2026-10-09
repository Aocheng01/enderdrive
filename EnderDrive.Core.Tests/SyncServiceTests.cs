using EnderDrive.Core.Cloud;
using EnderDrive.Core.Models;
using EnderDrive.Core.Services;
using EnderDrive.Core.Tests.Helpers;

namespace EnderDrive.Core.Tests;

public sealed class SyncServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeCloudProvider _cloud = new();
    private readonly BackupService _backups;
    private readonly SyncService _sync;
    private readonly string _world;

    public SyncServiceTests()
    {
        _settings.Current.BackupsPath = _temp.Combine("backups");
        _backups = new BackupService(_settings);
        _sync = new SyncService(_backups, _cloud, _settings);
        _world = TestWorlds.Create121(_temp.Combine("saves"), "Mi Mundo");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Crea_una_copia_local_y_la_sube()
    {
        var uploaded = await _sync.UploadWorldAsync(_world);

        var local = Assert.Single(await _backups.GetBackupsAsync());
        Assert.Equal(local.FilePath, Assert.Single(_cloud.UploadedPaths));
        Assert.Equal("Mi Mundo", uploaded.WorldFolderName);
        Assert.Contains(uploaded, _cloud.Files);
    }

    [Fact]
    public async Task Informa_del_progreso_de_comprimir_y_de_subir()
    {
        var stages = new List<string>();

        await _sync.UploadWorldAsync(_world, new SyncProgress(p => stages.Add(p.Stage)));

        Assert.Contains("Comprimiendo", stages);
        Assert.Contains("Subiendo", stages);
        Assert.True(stages.IndexOf("Comprimiendo") < stages.IndexOf("Subiendo"));
    }

    [Fact]
    public async Task En_la_nube_guarda_solo_el_maximo_de_copias_por_mundo()
    {
        _settings.Current.MaxBackupsPerWorld = 2;

        var first = await _sync.UploadWorldAsync(_world);
        await _sync.UploadWorldAsync(_world);
        var third = await _sync.UploadWorldAsync(_world);

        Assert.Equal(2, _cloud.Files.Count);
        Assert.Contains(third, _cloud.Files);
        Assert.Equal([first], _cloud.Trash); // la más antigua va a la papelera
    }

    [Fact]
    public async Task La_limpieza_no_toca_las_copias_de_otros_mundos()
    {
        _settings.Current.MaxBackupsPerWorld = 1;
        var other = TestWorlds.Create26(_temp.Combine("saves"), "Otro Mundo", seed: 1, hardcore: false);

        await _sync.UploadWorldAsync(other);
        await _sync.UploadWorldAsync(_world);
        await _sync.UploadWorldAsync(_world);

        Assert.Single(_cloud.Files, f => f.WorldFolderName == "Otro Mundo");
        Assert.Single(_cloud.Files, f => f.WorldFolderName == "Mi Mundo");
    }

    [Fact]
    public async Task Sin_sesion_no_crea_ninguna_copia()
    {
        _cloud.IsSignedIn = false;

        await Assert.ThrowsAsync<CloudException>(() => _sync.UploadWorldAsync(_world));

        Assert.Empty(await _backups.GetBackupsAsync());
        Assert.Empty(_cloud.UploadedPaths);
    }

    [Fact]
    public async Task Con_el_mundo_abierto_no_sube_nada()
    {
        using var sessionLock = new FileStream(Path.Combine(_world, "session.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        await Assert.ThrowsAsync<WorldInUseException>(() => _sync.UploadWorldAsync(_world));

        Assert.Empty(_cloud.UploadedPaths);
    }

    [Fact]
    public async Task Un_mundo_que_nunca_se_subio_esta_solo_en_local()
    {
        var status = await _sync.GetStatusAsync(_world, _cloud.Files);

        Assert.Equal(SyncState.LocalOnly, status.State);
        Assert.Null(status.LastUpload);
    }

    [Fact]
    public async Task Justo_despues_de_subirlo_esta_sincronizado()
    {
        var uploaded = await _sync.UploadWorldAsync(_world);

        var status = await _sync.GetStatusAsync(_world, _cloud.Files);

        Assert.Equal(SyncState.Synced, status.State);
        Assert.Equal(uploaded, status.LastUpload);
        Assert.NotNull(uploaded.Fingerprint);
    }

    [Fact]
    public async Task Si_se_juega_despues_de_subirlo_tiene_cambios_pendientes()
    {
        await _sync.UploadWorldAsync(_world);
        File.WriteAllBytes(Path.Combine(_world, "region", "r.5.5.mca"), new byte[4096]);

        var status = await _sync.GetStatusAsync(_world, _cloud.Files);

        Assert.Equal(SyncState.PendingChanges, status.State);
        Assert.True(status.PendingBytes >= 4096);
    }

    [Fact]
    public async Task Compara_con_la_copia_mas_reciente_del_mismo_mundo()
    {
        var other = TestWorlds.Create26(_temp.Combine("saves"), "Otro Mundo", seed: 1, hardcore: false);
        await _sync.UploadWorldAsync(_world);
        File.AppendAllText(Path.Combine(_world, "level.dat"), "x");
        await _sync.UploadWorldAsync(_world); // la más reciente ya incluye el cambio
        await _sync.UploadWorldAsync(other);

        var status = await _sync.GetStatusAsync(_world, _cloud.Files);

        Assert.Equal(SyncState.Synced, status.State);
    }

    [Fact]
    public async Task Una_copia_antigua_sin_huella_cuenta_como_cambios_pendientes()
    {
        // Copias subidas antes de que existiera la huella
        _cloud.Files.Add(new CloudBackup("viejo", "Mi Mundo", "viejo.zip",
            DateTime.Now.AddDays(-1), 100, BackupReason.Manual, Fingerprint: null));

        var status = await _sync.GetStatusAsync(_world, _cloud.Files);

        Assert.Equal(SyncState.PendingChanges, status.State);
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public void Save()
        {
        }
    }

    private sealed class SyncProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
