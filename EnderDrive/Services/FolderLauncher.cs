using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace EnderDrive.Services
{
    public sealed class FolderLauncher : IFolderLauncher
    {
        public async Task OpenAsync(string folderPath)
        {
            if (MainWindowLocator.Get() is not { } window || !Directory.Exists(folderPath))
                return;

            // Launcher abre la carpeta con la app por defecto del sistema (Explorador en Windows)
            await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folderPath));
        }
    }
}
