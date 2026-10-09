namespace EnderDrive.Core.Tests.Helpers;

/// <summary>
/// Carpeta temporal única para un test. Se borra sola al terminar gracias a IDisposable:
/// <c>using var temp = new TempDirectory();</c>
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "EnderDrive.Tests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Si algún archivo sigue abierto, no hacemos fallar el test por la limpieza
        }
    }
}
