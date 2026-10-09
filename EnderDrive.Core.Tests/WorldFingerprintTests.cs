using EnderDrive.Core.Models;
using EnderDrive.Core.Services;
using EnderDrive.Core.Tests.Helpers;

namespace EnderDrive.Core.Tests;

public sealed class WorldFingerprintTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _world;

    public WorldFingerprintTests() => _world = TestWorlds.Create121(_temp.Combine("saves"), "Mundo");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void El_mismo_mundo_da_siempre_la_misma_huella()
    {
        Assert.Equal(WorldFingerprint.Compute(_world), WorldFingerprint.Compute(_world));
    }

    [Fact]
    public void Cambia_si_cambia_el_tamaño_de_un_archivo()
    {
        var before = WorldFingerprint.Compute(_world);
        File.AppendAllText(Path.Combine(_world, "level.dat"), "x");

        Assert.NotEqual(before, WorldFingerprint.Compute(_world));
    }

    [Fact]
    public void Cambia_si_se_modifica_un_archivo_aunque_ocupe_lo_mismo()
    {
        var region = Path.Combine(_world, "region", "r.0.0.mca");
        var before = WorldFingerprint.Compute(_world);
        File.SetLastWriteTimeUtc(region, File.GetLastWriteTimeUtc(region).AddMinutes(5));

        Assert.NotEqual(before, WorldFingerprint.Compute(_world));
    }

    [Fact]
    public void Cambia_si_aparece_un_archivo_nuevo()
    {
        var before = WorldFingerprint.Compute(_world);
        File.WriteAllText(Path.Combine(_world, "region", "r.1.0.mca"), "nuevo");

        Assert.NotEqual(before, WorldFingerprint.Compute(_world));
    }

    [Fact]
    public void Ignora_session_lock_y_temporales()
    {
        var before = WorldFingerprint.Compute(_world);
        File.WriteAllText(Path.Combine(_world, "session.lock"), "abierto otra vez");
        File.WriteAllText(Path.Combine(_world, "level.dat.tmp"), "temporal");

        Assert.Equal(before, WorldFingerprint.Compute(_world));
    }

    [Fact]
    public async Task Un_mundo_restaurado_tiene_la_misma_huella_que_el_original()
    {
        // El zip guarda las fechas con 2 segundos de precisión; la huella está pensada para ello
        var settings = new FakeSettings();
        settings.Current.BackupsPath = _temp.Combine("backups");
        var service = new BackupService(settings);

        var original = WorldFingerprint.Compute(_world);
        var backup = await service.CreateBackupAsync(_world);
        await service.RestoreAsync(backup, _temp.Combine("saves"));

        Assert.Equal(original, WorldFingerprint.Compute(_world));
    }

    [Fact]
    public void Suma_solo_lo_que_cambio_despues_de_una_fecha()
    {
        var since = DateTime.UtcNow.AddMinutes(-1);
        File.SetLastWriteTimeUtc(Path.Combine(_world, "region", "r.0.0.mca"), since.AddMinutes(-10)); // antes
        File.SetLastWriteTimeUtc(Path.Combine(_world, "icon.png"), since.AddMinutes(-10));             // antes
        File.SetLastWriteTimeUtc(Path.Combine(_world, "level.dat"), since.AddMinutes(-10));            // antes
        File.WriteAllBytes(Path.Combine(_world, "nuevo.dat"), new byte[1000]);                         // después

        Assert.Equal(1000, WorldFingerprint.ChangedBytesSince(_world, since));
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public void Save()
        {
        }
    }
}
