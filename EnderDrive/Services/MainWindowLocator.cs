using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace EnderDrive.Services
{
    /// <summary>Da acceso a la ventana principal a los servicios que la necesitan (diálogos, lanzador…).</summary>
    internal static class MainWindowLocator
    {
        public static Window? Get()
            => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
    }
}
