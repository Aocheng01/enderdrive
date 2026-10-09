namespace EnderDrive.Core.Tests.Helpers;

/// <summary>Fabrica carpetas de mundo falsas con el formato de distintas versiones de Minecraft.</summary>
public static class TestWorlds
{
    public static readonly DateTime LastPlayed = new(2025, 1, 12, 18, 30, 0, DateTimeKind.Local);

    public static long LastPlayedMs => new DateTimeOffset(LastPlayed).ToUnixTimeMilliseconds();

    /// <summary>Formato 1.16–1.21: semilla en Data.WorldGenSettings, hardcore en Data.</summary>
    public static string Create121(string savesPath, string folder, string levelName = "Mundo de prueba",
        long seed = 123456789, bool hardcore = false, int gameType = 0, string brand = "vanilla")
    {
        var world = Path.Combine(savesPath, folder);
        Nbt.WriteFile(Path.Combine(world, "level.dat"), new Nbt
        {
            ["Data"] = new Nbt
            {
                ["LevelName"] = levelName,
                ["LastPlayed"] = LastPlayedMs,
                ["GameType"] = gameType,
                ["hardcore"] = (sbyte)(hardcore ? 1 : 0),
                ["ServerBrands"] = new List<object> { brand },
                ["Version"] = new Nbt { ["Name"] = "1.21.1", ["Id"] = 3955 },
                ["WorldGenSettings"] = new Nbt { ["seed"] = seed, ["bonus_chest"] = (sbyte)0 },
            },
        });
        AddRegionAndIcon(world);
        return world;
    }

    /// <summary>
    /// Formato 26.x: la semilla se mudó a data/minecraft/world_gen_settings.dat
    /// y el hardcore a Data.difficulty_settings.
    /// </summary>
    public static string Create26(string savesPath, string folder, long seed, bool hardcore)
    {
        var world = Path.Combine(savesPath, folder);
        Nbt.WriteFile(Path.Combine(world, "level.dat"), new Nbt
        {
            ["Data"] = new Nbt
            {
                ["LevelName"] = folder,
                ["LastPlayed"] = LastPlayedMs,
                ["GameType"] = 1,
                ["ServerBrands"] = new List<object> { "vanilla" },
                ["Version"] = new Nbt { ["Name"] = "26.3" },
                ["difficulty_settings"] = new Nbt
                {
                    ["difficulty"] = "normal",
                    ["hardcore"] = (sbyte)(hardcore ? 1 : 0),
                },
            },
        });
        Nbt.WriteFile(Path.Combine(world, "data", "minecraft", "world_gen_settings.dat"), new Nbt
        {
            ["data"] = new Nbt { ["seed"] = seed },
            ["DataVersion"] = 5023,
        });
        return world;
    }

    /// <summary>Formato antiguo (antes de 1.16): la semilla está en Data.RandomSeed.</summary>
    public static string CreateLegacy(string savesPath, string folder, long seed)
    {
        var world = Path.Combine(savesPath, folder);
        Nbt.WriteFile(Path.Combine(world, "level.dat"), new Nbt
        {
            ["Data"] = new Nbt
            {
                ["LevelName"] = folder,
                ["RandomSeed"] = seed,
                ["LastPlayed"] = LastPlayedMs,
                ["GameType"] = 0,
            },
        });
        return world;
    }

    private static void AddRegionAndIcon(string world)
    {
        // Un poco de "contenido" para que las copias tengan varios archivos y subcarpetas
        var region = Path.Combine(world, "region");
        Directory.CreateDirectory(region);
        File.WriteAllBytes(Path.Combine(region, "r.0.0.mca"), Enumerable.Range(0, 64 * 1024).Select(i => (byte)i).ToArray());
        File.WriteAllBytes(Path.Combine(world, "icon.png"), [0x89, 0x50, 0x4E, 0x47]);
        File.WriteAllText(Path.Combine(world, "session.lock"), "☃");
    }
}
