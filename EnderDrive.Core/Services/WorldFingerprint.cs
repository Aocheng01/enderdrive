using System.Security.Cryptography;
using System.Text;

namespace EnderDrive.Core.Services;

/// <summary>
/// "Huella" rápida de un mundo: un resumen de qué archivos tiene, cuánto ocupan y cuándo
/// se modificaron, SIN leer su contenido. Si cambia cualquier archivo, cambia la huella.
/// Sirve para saber en milisegundos si un mundo ha cambiado desde que se subió a la nube.
/// </summary>
public static class WorldFingerprint
{
    // El formato .zip guarda las fechas con precisión de 2 segundos. Redondeamos igual,
    // para que un mundo restaurado desde una copia tenga la misma huella que el original.
    private static readonly long Resolution = TimeSpan.FromSeconds(2).Ticks;

    /// <summary>Calcula la huella (32 caracteres hexadecimales).</summary>
    public static string Compute(string worldFolder)
    {
        var builder = new StringBuilder();

        // Ordenamos por ruta: el orden en que Windows lista los archivos no está garantizado
        foreach (var file in EnumerateWorldFiles(worldFolder).OrderBy(f => f.RelativePath, StringComparer.Ordinal))
            builder.Append(file.RelativePath).Append('|').Append(file.Size).Append('|').Append(file.Stamp).Append('\n');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    /// <summary>Suma lo que ocupan los archivos modificados después de <paramref name="sinceUtc"/>.</summary>
    /// <remarks>
    /// Usamos &gt;= y no &gt;: con el redondeo a 2 segundos, un archivo modificado justo después de
    /// la copia puede caer en el mismo "tramo". Es una estimación: preferimos contar de más que de menos.
    /// </remarks>
    public static long ChangedBytesSince(string worldFolder, DateTime sinceUtc)
    {
        var since = Round(sinceUtc.ToUniversalTime());
        return EnumerateWorldFiles(worldFolder).Where(f => f.Stamp >= since).Sum(f => f.Size);
    }

    private static IEnumerable<(string RelativePath, long Size, long Stamp)> EnumerateWorldFiles(string worldFolder)
        => new DirectoryInfo(worldFolder)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !IsIgnored(f.Name))
            .Select(f => (
                Path.GetRelativePath(worldFolder, f.FullName).Replace('\\', '/'),
                f.Length,
                Round(f.LastWriteTimeUtc)));

    /// <summary>Archivos que cambian sin que cambie el mundo: no deben contar como "cambios".</summary>
    private static bool IsIgnored(string fileName)
        => fileName.Equals("session.lock", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    private static long Round(DateTime utc) => utc.Ticks / Resolution;
}
