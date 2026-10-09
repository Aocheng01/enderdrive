using EnderDrive.Core.Models;
using EnderDrive.Core.Services;
using EnderDrive.Core.Tests.Helpers;

namespace EnderDrive.Core.Tests;

public sealed class WorldActivityMonitorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly BackupService _backups;
    private readonly WorldActivityMonitor _monitor;
    private readonly string _saves;
    private readonly string _world;
    private readonly List<string> _events = [];

    public WorldActivityMonitorTests()
    {
        var settings = new FakeSettings();
        settings.Current.BackupsPath = _temp.Combine("backups");
        _backups = new BackupService(settings);
        _monitor = new WorldActivityMonitor(_backups);
        _monitor.WorldOpened += w => _events.Add("abierto " + Path.GetFileName(w));
        _monitor.WorldClosed += w => _events.Add("cerrado " + Path.GetFileName(w));

        _saves = _temp.Combine("saves");
        _world = TestWorlds.Create121(_saves, "Mi Mundo");
        TestWorlds.Create121(_saves, "Otro Mundo");
    }

    public void Dispose()
    {
        _monitor.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void Avisa_al_abrir_y_al_cerrar_un_mundo()
    {
        _monitor.CheckNow(_saves);

        using (OpenInMinecraft(_world))
        {
            _monitor.CheckNow(_saves);
            Assert.True(_monitor.IsOpen(_world));
        }
        _monitor.CheckNow(_saves);

        Assert.Equal(["abierto Mi Mundo", "cerrado Mi Mundo"], _events);
        Assert.False(_monitor.IsOpen(_world));
    }

    [Fact]
    public void No_repite_el_aviso_mientras_el_mundo_sigue_abierto()
    {
        using (OpenInMinecraft(_world))
        {
            _monitor.CheckNow(_saves);
            _monitor.CheckNow(_saves);
            _monitor.CheckNow(_saves);
        }

        Assert.Equal(["abierto Mi Mundo"], _events);
    }

    [Fact]
    public void Sin_mundos_abiertos_no_avisa_de_nada()
    {
        _monitor.CheckNow(_saves);
        _monitor.CheckNow(_saves);

        Assert.Empty(_events);
    }

    [Fact]
    public async Task En_segundo_plano_detecta_el_cierre_solo()
    {
        // Un vigilante rápido (cada 50 ms) para no esperar 5 segundos en el test
        using var monitor = new WorldActivityMonitor(_backups, TimeSpan.FromMilliseconds(50));
        var closed = new TaskCompletionSource<string>();
        monitor.WorldClosed += w => closed.TrySetResult(w);

        using (OpenInMinecraft(_world))
        {
            monitor.Start(_saves);
            await Task.Delay(300); // le da tiempo a ver el mundo abierto
        }

        var result = await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(_world, result, ignoreCase: true);
    }

    /// <summary>Mantiene session.lock abierto, como hace Minecraft mientras juegas.</summary>
    private static FileStream OpenInMinecraft(string world)
        => new(Path.Combine(world, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public void Save()
        {
        }
    }
}
