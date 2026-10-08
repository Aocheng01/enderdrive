using System.Text.RegularExpressions;
using EnderDrive.Core.Models;
using EnderDrive.Core.Nbt;

namespace EnderDrive.Core.Services;

/// <summary>Datos que nos interesan de level.dat. Cualquier campo puede faltar en mundos antiguos.</summary>
internal sealed record LevelData(
    string? LevelName,
    DateTime? LastPlayed,
    string? GameVersion,
    GameMode? GameMode,
    bool IsHardcore,
    long? Seed,
    string? Loader);

/// <summary>
/// Lee los datos de un mundo. Minecraft ha cambiado dónde guarda algunos campos:
/// <list type="bullet">
/// <item>Semilla: Data.RandomSeed (antes de 1.16), Data.WorldGenSettings.seed (1.16–1.21)
/// y data/minecraft/world_gen_settings.dat (26.x).</item>
/// <item>Hardcore: Data.hardcore (hasta 1.21) y Data.difficulty_settings.hardcore (26.x).</item>
/// </list>
/// </summary>
internal static partial class LevelDatReader
{
    /// <summary>Devuelve null si level.dat está dañado o no se puede leer.</summary>
    public static LevelData? TryRead(string worldFolder)
    {
        try
        {
            var data = NbtReader.ReadFile(Path.Combine(worldFolder, "level.dat")).GetCompound("Data");
            if (data is null)
                return null;

            var lastPlayedMs = data.GetLong("LastPlayed");
            var gameType = data.GetInt("GameType");

            return new LevelData(
                LevelName: CleanName(data.GetString("LevelName")),
                LastPlayed: lastPlayedMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(lastPlayedMs.Value).LocalDateTime
                    : null,
                GameVersion: data.GetCompound("Version")?.GetString("Name"),
                GameMode: gameType is >= 0 and <= 3 ? (GameMode)gameType.Value : null,
                IsHardcore: data.GetBool("hardcore")
                    ?? data.GetCompound("difficulty_settings")?.GetBool("hardcore")
                    ?? false,
                Seed: data.GetLong("RandomSeed")
                    ?? data.GetCompound("WorldGenSettings")?.GetLong("seed")
                    ?? ReadSeedFromWorldGenFile(worldFolder),
                Loader: data.GetList("ServerBrands")?.OfType<string>().FirstOrDefault());
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long? ReadSeedFromWorldGenFile(string worldFolder)
    {
        var path = Path.Combine(worldFolder, "data", "minecraft", "world_gen_settings.dat");
        if (!File.Exists(path))
            return null;

        try
        {
            return NbtReader.ReadFile(path).GetCompound("data")?.GetLong("seed");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Quita los códigos de color de Minecraft (§a, §l…) y espacios sobrantes.</summary>
    private static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return FormattingCodes().Replace(name, "").Trim();
    }

    [GeneratedRegex("§.")]
    private static partial Regex FormattingCodes();
}
