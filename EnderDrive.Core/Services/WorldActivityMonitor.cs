namespace EnderDrive.Core.Services;

/// <summary>
/// Vigila qué mundos tiene abiertos Minecraft. No hay un evento del sistema que avise
/// cuando un programa suelta un archivo, así que preguntamos cada pocos segundos
/// ("polling") si el session.lock de cada mundo sigue bloqueado.
/// </summary>
/// <param name="interval">Cada cuánto se comprueba (5 segundos si no se indica).</param>
public sealed class WorldActivityMonitor(IBackupService backups, TimeSpan? interval = null) : IDisposable
{
    private readonly HashSet<string> _openWorlds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _loop;
    private string? _savesPath;

    /// <summary>Se abrió un mundo en Minecraft (ruta completa de la carpeta).</summary>
    /// <remarks>Se lanza desde un hilo en segundo plano.</remarks>
    public event Action<string>? WorldOpened;

    /// <summary>Se cerró un mundo en Minecraft: buen momento para subir los cambios.</summary>
    /// <remarks>Se lanza desde un hilo en segundo plano.</remarks>
    public event Action<string>? WorldClosed;

    public TimeSpan Interval { get; } = interval ?? TimeSpan.FromSeconds(5);

    public bool IsOpen(string worldFolder)
    {
        lock (_openWorlds)
            return _openWorlds.Contains(worldFolder);
    }

    /// <summary>Empieza a vigilar una carpeta saves (si ya vigilaba otra, la cambia).</summary>
    public void Start(string savesPath)
    {
        Stop();
        _savesPath = savesPath;
        _loop = new CancellationTokenSource();
        _ = RunAsync(_loop.Token);
    }

    public void Stop()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
        lock (_openWorlds)
            _openWorlds.Clear();
    }

    /// <summary>Comprueba ahora mismo todos los mundos. Público para poder probarlo sin esperar.</summary>
    public void CheckNow(string savesPath)
    {
        if (!Directory.Exists(savesPath))
            return;

        var nowOpen = Directory.EnumerateDirectories(savesPath)
            .Where(backups.IsWorldInUse)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> opened, closed;
        lock (_openWorlds)
        {
            opened = nowOpen.Except(_openWorlds, StringComparer.OrdinalIgnoreCase).ToList();
            closed = _openWorlds.Except(nowOpen, StringComparer.OrdinalIgnoreCase).ToList();
            _openWorlds.Clear();
            _openWorlds.UnionWith(nowOpen);
        }

        // Avisamos fuera del lock, para que quien escuche pueda llamar a IsOpen sin bloquearse
        foreach (var world in opened)
            WorldOpened?.Invoke(world);
        foreach (var world in closed)
            WorldClosed?.Invoke(world);
    }

    public void Dispose() => Stop();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // PeriodicTimer: "despiértame cada X segundos" sin bloquear ningún hilo mientras espera
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    CheckNow(_savesPath!);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Carpeta inaccesible un momento: lo intentamos en la siguiente vuelta
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // Stop(): fin normal
        }
    }
}
