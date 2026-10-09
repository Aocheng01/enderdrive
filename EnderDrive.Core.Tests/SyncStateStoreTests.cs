using EnderDrive.Core.Services;
using EnderDrive.Core.Tests.Helpers;

namespace EnderDrive.Core.Tests;

public class SyncStateStoreTests
{
    [Fact]
    public void Guarda_la_base_y_la_recupera_al_volver_a_abrir()
    {
        using var temp = new TempDirectory();
        var file = temp.Combine("sync-state.json");
        var syncBase = new SyncBase("huella-local", "id-1", "huella-nube", new DateTime(2026, 1, 1));

        new JsonSyncStateStore(file).Set(temp.Combine("saves", "Mundo"), syncBase);

        Assert.Equal(syncBase, new JsonSyncStateStore(file).Get(temp.Combine("saves", "Mundo")));
    }

    [Fact]
    public void La_ruta_no_distingue_mayusculas()
    {
        using var temp = new TempDirectory();
        var store = new JsonSyncStateStore(temp.Combine("sync-state.json"));
        store.Set(temp.Combine("saves", "Mundo"), new SyncBase("a", "b", null, DateTime.Now));

        Assert.NotNull(store.Get(temp.Combine("SAVES", "mundo")));
    }

    [Fact]
    public void El_mismo_mundo_en_otra_carpeta_saves_es_otro_mundo()
    {
        using var temp = new TempDirectory();
        var store = new JsonSyncStateStore(temp.Combine("sync-state.json"));
        store.Set(temp.Combine("saves", "Mundo"), new SyncBase("a", "b", null, DateTime.Now));

        Assert.Null(store.Get(temp.Combine("otra-carpeta", "Mundo")));
    }

    [Fact]
    public void Un_archivo_danado_no_impide_arrancar()
    {
        using var temp = new TempDirectory();
        var file = temp.Combine("sync-state.json");
        File.WriteAllText(file, "{ esto no es json");

        Assert.Null(new JsonSyncStateStore(file).Get(temp.Combine("saves", "Mundo")));
    }
}
