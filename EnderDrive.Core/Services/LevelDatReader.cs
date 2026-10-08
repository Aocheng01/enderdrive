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
    bool IsHardcore);

internal static partial class LevelDatReader
{
    /// <summary>Lee level.dat. Devuelve null si el archivo está dañado o no se puede leer.</summary>
    public static LevelData? TryRead(string levelDatPath)
    {
        try
        {
            var data = NbtReader.ReadFile(levelDatPath).GetCompound("Data");
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
                IsHardcore: data.GetBool("hardcore") ?? false);
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
