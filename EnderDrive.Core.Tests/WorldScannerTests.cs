using EnderDrive.Core.Models;
using EnderDrive.Core.Services;
using EnderDrive.Core.Tests.Helpers;
using NbtData = EnderDrive.Core.Tests.Helpers.Nbt;

namespace EnderDrive.Core.Tests;

/// <summary>Prueba WorldScanner y, a través de él, LevelDatReader con los formatos de cada versión.</summary>
public class WorldScannerTests
{
    private readonly WorldScanner _scanner = new();

    [Fact]
    public async Task Lee_un_mundo_de_la_1_21()
    {
        using var temp = new TempDirectory();
        TestWorlds.Create121(temp.Path, "carpeta", levelName: "Valle Esmeralda", seed: -4829104829,
            gameType: 1, brand: "neoforge");

        var world = Assert.Single(await _scanner.ScanAsync(temp.Path));

        Assert.Equal("Valle Esmeralda", world.Name);
        Assert.Equal("carpeta", world.FolderName);
        Assert.Equal(-4829104829, world.Seed);
        Assert.Equal("1.21.1", world.GameVersion);
        Assert.Equal(GameMode.Creative, world.GameMode);
        Assert.Equal("neoforge", world.Loader);
        Assert.Equal(TestWorlds.LastPlayed, world.LastPlayed);
        Assert.False(world.IsHardcore);
        Assert.NotNull(world.IconPath);
        Assert.True(world.SizeBytes > 64 * 1024);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Lee_semilla_y_hardcore_del_formato_26(bool hardcore)
    {
        using var temp = new TempDirectory();
        TestWorlds.Create26(temp.Path, "Mundo 26", seed: 7333592153913774754, hardcore: hardcore);

        var world = Assert.Single(await _scanner.ScanAsync(temp.Path));

        Assert.Equal(7333592153913774754, world.Seed);
        Assert.Equal(hardcore, world.IsHardcore);
        Assert.Equal("26.3", world.GameVersion);
    }

    [Fact]
    public async Task Lee_la_semilla_del_formato_antiguo()
    {
        using var temp = new TempDirectory();
        TestWorlds.CreateLegacy(temp.Path, "Viejo", seed: 42);

        var world = Assert.Single(await _scanner.ScanAsync(temp.Path));

        Assert.Equal(42, world.Seed);
        Assert.Null(world.GameVersion);
    }

    [Fact]
    public async Task Quita_los_codigos_de_color_del_nombre()
    {
        using var temp = new TempDirectory();
        TestWorlds.Create121(temp.Path, "c", levelName: "§aMundo §lVerde");

        var world = Assert.Single(await _scanner.ScanAsync(temp.Path));

        Assert.Equal("Mundo Verde", world.Name);
    }

    [Fact]
    public async Task Con_un_level_dat_corrupto_usa_el_nombre_de_la_carpeta()
    {
        using var temp = new TempDirectory();
        var folder = Directory.CreateDirectory(temp.Combine("Roto")).FullName;
        File.WriteAllText(Path.Combine(folder, "level.dat"), "esto no es NBT");

        var world = Assert.Single(await _scanner.ScanAsync(temp.Path));

        Assert.Equal("Roto", world.Name);
        Assert.Null(world.Seed);
    }

    [Fact]
    public async Task Ignora_carpetas_sin_level_dat()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("no-es-un-mundo"));
        TestWorlds.Create121(temp.Path, "si-es-un-mundo");

        var world = Assert.Single(await _scanner.ScanAsync(temp.Path));

        Assert.Equal("si-es-un-mundo", world.FolderName);
    }

    [Fact]
    public async Task Ordena_del_mas_reciente_al_mas_antiguo()
    {
        using var temp = new TempDirectory();
        var older = TestWorlds.CreateLegacy(temp.Path, "Antiguo", seed: 1);
        TestWorlds.Create121(temp.Path, "Reciente");

        // Al antiguo le ponemos una fecha de última partida anterior
        NbtData.WriteFile(Path.Combine(older, "level.dat"), new NbtData
        {
            ["Data"] = new NbtData { ["LastPlayed"] = TestWorlds.LastPlayedMs - 86_400_000 },
        });

        var worlds = await _scanner.ScanAsync(temp.Path);

        Assert.Equal(["Reciente", "Antiguo"], worlds.Select(w => w.FolderName));
    }

    [Fact]
    public async Task Una_carpeta_que_no_existe_devuelve_lista_vacia()
    {
        var worlds = await _scanner.ScanAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.Empty(worlds);
    }
}
