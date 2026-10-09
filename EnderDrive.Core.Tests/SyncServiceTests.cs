using EnderDrive.Core.Cloud;
using EnderDrive.Core.Models;
using EnderDrive.Core.Services;
using EnderDrive.Core.Tests.Helpers;

namespace EnderDrive.Core.Tests;

public sealed class SyncServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeCloudProvider _cloud;
    private readonly JsonSyncStateStore _syncState;
    private readonly BackupService _backups;
    private readonly SyncService _sync;
    private readonly string _saves;
    private readonly string _world;

    public SyncServiceTests()
    {
        _settings.Current.BackupsPath = _temp.Combine("backups");
        _cloud = new FakeCloudProvider(_temp.Combine("cloud"));
        _syncState = new JsonSyncStateStore(_temp.Combine("sync-state.json"));
        _backups = new BackupService(_settings);
        _sync = new SyncService(_backups, _cloud, _settings, _syncState);
        _saves = _temp.Combine("saves");
        _world = TestWorlds.Create121(_saves, "Mi Mundo");
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

    // ===== Paso 4: base de sincronización y descargas =====

    [Fact]
    public async Task Si_otro_PC_sube_cambios_hay_una_version_mas_nueva_en_la_nube()
    {
        await _sync.UploadWorldAsync(_world);
        await UploadFromOtherPcAsync();

        var status = await _sync.GetStatusAsync(_world, _cloud.Files);

        Assert.Equal(SyncState.CloudNewer, status.State);
    }

    [Fact]
    public async Task Si_cambian_los_dos_lados_hay_conflicto()
    {
        await _sync.UploadWorldAsync(_world);
        await UploadFromOtherPcAsync();
        File.AppendAllText(Path.Combine(_world, "level.dat"), "cambio local");

        var status = await _sync.GetStatusAsync(_world, _cloud.Files);

        Assert.Equal(SyncState.Conflict, status.State);
    }

    [Fact]
    public async Task Sin_base_y_con_la_misma_huella_esta_sincronizado_y_guarda_la_base()
    {
        // Mundo subido antes de que existieran las bases: la nube tiene su huella, pero este PC no tiene base
        var backup = await _backups.CreateBackupAsync(_world);
        _cloud.AddRemoteBackup("Mi Mundo", backup.FilePath, WorldFingerprint.Compute(_world));

        var status = await _sync.GetStatusAsync(_world, _cloud.Files);

        Assert.Equal(SyncState.Synced, status.State);
        Assert.NotNull(_syncState.Get(_world));
    }

    [Fact]
    public async Task Sin_base_y_con_huellas_distintas_hay_conflicto()
    {
        await UploadFromOtherPcAsync();

        var status = await _sync.GetStatusAsync(_world, _cloud.Files);

        Assert.Equal(SyncState.Conflict, status.State);
    }

    [Fact]
    public async Task Descarga_un_mundo_que_solo_esta_en_la_nube()
    {
        var uploaded = await _sync.UploadWorldAsync(_world);
        var otherPcSaves = _temp.Combine("saves-otro-pc"); // un PC nuevo, sin mundos

        await _sync.DownloadWorldAsync(uploaded, otherPcSaves);

        var downloaded = Path.Combine(otherPcSaves, "Mi Mundo");
        Assert.True(File.Exists(Path.Combine(downloaded, "level.dat")));
        Assert.Equal(SyncState.Synced, (await _sync.GetStatusAsync(downloaded, _cloud.Files)).State);
    }

    [Fact]
    public async Task Descargar_cambios_sustituye_el_mundo_y_guarda_antes_una_copia()
    {
        await _sync.UploadWorldAsync(_world);
        var remote = await UploadFromOtherPcAsync();

        await _sync.DownloadWorldAsync(remote, _saves);

        Assert.True(File.Exists(Path.Combine(_world, "desde-otro-pc.txt")));
        Assert.Contains(await _backups.GetBackupsAsync(), b => b.Reason == BackupReason.BeforeRestore);
        Assert.Equal(SyncState.Synced, (await _sync.GetStatusAsync(_world, _cloud.Files)).State);
    }

    [Fact]
    public async Task Bajar_una_version_antigua_deja_cambios_pendientes_para_subirla()
    {
        var old = await _sync.UploadWorldAsync(_world);
        File.AppendAllText(Path.Combine(_world, "level.dat"), "x");
        await _sync.UploadWorldAsync(_world);

        await _sync.DownloadWorldAsync(old, _saves);

        // No "hay una versión más nueva en la nube": el usuario eligió volver atrás a propósito
        Assert.Equal(SyncState.PendingChanges, (await _sync.GetStatusAsync(_world, _cloud.Files)).State);
    }

    [Fact]
    public async Task No_descarga_encima_de_un_mundo_abierto()
    {
        var uploaded = await _sync.UploadWorldAsync(_world);
        File.WriteAllText(Path.Combine(_world, "partida.txt"), "en curso");

        using (new FileStream(Path.Combine(_world, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
            await Assert.ThrowsAsync<WorldInUseException>(() => _sync.DownloadWorldAsync(uploaded, _saves));

        Assert.True(File.Exists(Path.Combine(_world, "partida.txt")));
    }

    [Fact]
    public async Task Lista_los_mundos_que_solo_estan_en_la_nube()
    {
        var other = TestWorlds.Create26(_saves, "Otro Mundo", seed: 1, hardcore: false);
        await _sync.UploadWorldAsync(_world);
        await _sync.UploadWorldAsync(other);
        var latestOther = await _sync.UploadWorldAsync(other);

        var cloudOnly = _sync.GetCloudOnlyWorlds(_cloud.Files, ["Mi Mundo"]);

        Assert.Equal([latestOther], cloudOnly); // solo la copia más reciente de cada mundo
    }

    /// <summary>
    /// Simula que en otro PC se jugó en este mundo y se subió: misma carpeta, contenido distinto.
    /// </summary>
    private async Task<CloudBackup> UploadFromOtherPcAsync()
    {
        var otherPcSaves = _temp.Combine("otro-pc");
        var otherWorld = TestWorlds.Create121(otherPcSaves, "Mi Mundo");
        File.WriteAllText(Path.Combine(otherWorld, "desde-otro-pc.txt"), "jugado en otro PC");

        var otherSettings = new FakeSettings();
        otherSettings.Current.BackupsPath = _temp.Combine("backups-otro-pc");
        var zip = await new BackupService(otherSettings).CreateBackupAsync(otherWorld);

        return _cloud.AddRemoteBackup("Mi Mundo", zip.FilePath, WorldFingerprint.Compute(otherWorld));
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
